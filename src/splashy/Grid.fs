namespace splashy

open System.Collections.Generic

open Constants
open Vector
open Coord

module Grid =

  type Media = Air | Fluid | Solid

  type Cell = { pressure: float;
                media: Media;
                velocity: Vector3d; // from the minimal faces, not the center
                layer: Option<int>; }

  let default_cell = { pressure = 0.0; media = Air; layer = None; velocity = Vector3d() }

  let max_distance = Operators.max 2 (int (ceil Constants.time_step_constant))

  let mutable grid = new Dictionary<Coord, Cell>()

  let add where c = grid.Add (where, c)

  let delete where = let _ = grid.Remove where in ()

  let get where =
    match grid.ContainsKey where with
      | true -> Some grid.[where]
      | _ -> None

  let set where c =
    match get where with
      | None -> failwith "Tried to set non-existent cell."
      | _ -> grid.[where] <- c

  let filter_values fn =
    let result = Seq.filter (fun (KeyValue(k, v)) -> fn v) grid
    let keys = Seq.map (fun (KeyValue(k, v)) -> k) result
    // make a copy; we want to avoid writing to the dictionary while
    // potentially iterating over it.
    new List<Coord> (keys)

  let is_solid c = match c.media with Solid -> true | _ -> false

  let setup fn =
    try
      // reset grid layers.
      let coords = filter_values (fun _ -> true)
      Seq.iter (fun m ->
                match get m with
                  | Some c -> set m { c with layer = None }
                  | _ -> failwith "Could not get/set grid cell."
                ) coords
      fn ()
    finally
      // get rid of unused cells.
      let leftover = filter_values (fun c -> c.layer = None)
      Seq.iter delete leftover

  let cleanup () =
    // reset layers.
    let coords = filter_values (fun _ -> true)
    Seq.iter (fun m ->
                match get m with
                  | Some c when c.media = Fluid -> set m { c with layer = Some 0 }
                  | Some c -> set m { c with layer = None }
                  | _ -> failwith "Could not get/set grid cell."
                ) coords
    // extrapolate fluid velocities into surrounding cells.
    for i in 1..max_distance do
      let nonfluid = filter_values (fun c -> c.layer = None)
      Seq.iter (fun (m: Coord) ->
                let neighbors = m.neighbors ()
                let previous_layer = Seq.filter (fun (_, c) -> match get c with
                                                                 | Some c when c.layer = Some (i - 1) -> true
                                                                 | _ -> false
                                                 ) neighbors
                let n = Seq.length previous_layer
                if n <> 0 then
                  let velocities = Seq.map (fun (_, where) -> match get where with
                                                                | Some c -> c.velocity
                                                                | None -> failwith "Internal error.") previous_layer
                  let previous_layer_average = (Seq.fold (.+) (Vector3d()) velocities) ./ float n
                  for dir, neighbor in neighbors do
                    match get neighbor with
                      | Some c when c.media = Fluid && Coord.is_bordering dir c.velocity ->
                        let new_v = match dir with
                                      | NegX | PosX -> Vector3d(previous_layer_average.x, c.velocity.y, c.velocity.z)
                                      | NegY | PosY -> Vector3d(c.velocity.x, previous_layer_average.y, c.velocity.z)
                                      | NegZ | PosZ -> Vector3d(c.velocity.x, c.velocity.y, previous_layer_average.z)
                        set neighbor { c with velocity = new_v }
                      | _ -> ()
                  let c = get m |> Option.get
                  set m { c with layer = Some (i - 1) }
                ) nonfluid
    // set velocities of solid cells to zero.
    let solids = Seq.filter (fun m -> match get m with
                                        | Some c when is_solid c -> true
                                        | _ -> false) coords
    Seq.iter (fun (m: Coord) ->
              let neighbors = m.neighbors ()
              for (_, neighbor) in neighbors do
                match get neighbor with
                  | Some c when not (is_solid c) -> set neighbor { c with velocity = Vector3d() }
                  | _ -> ()
              ) solids

  let internal get_velocity_index where index =
    match get where with
      | Some c when is_solid c ->
        match index with
          | 0 -> Some c.velocity.x
          | 1 -> Some c.velocity.y
          | 2 -> Some c.velocity.z
          | _ -> failwith "No such index."
      | _ -> None

  let internal interpolate x y z index =
    let i = floor x
    let j = floor y
    let k = floor z
    let ii = int i
    let jj = int j
    let kk = int k
    let id = [i - x + 1.0; x - i]
    let jd = [j - y + 1.0; y - j]
    let kd = [k - z + 1.0; z - k]
    // trilinear interpolation
    let sums = [for x' in 0..1 do
                for y' in 0..1 do
                for z' in 0..1 do
                let c = { x = ii + x'; y = jj + y'; z = kk + z' }
                match get_velocity_index c index with
                  | Some v -> yield (v * id.[x'] * jd.[y'] * kd.[z'])
                  | None -> yield 0.0]
    Seq.sum sums

  let internal get_interpolated_velocity x y z =
    let xh = float x / Constants.h
    let yh = float y / Constants.h
    let zh = float z / Constants.h
    let x = interpolate xh (yh - 0.5) (zh - 0.5) 0
    let y = interpolate (xh - 0.5) yh (zh - 0.5) 1
    let z = interpolate (xh - 0.5) (yh - 0.5) zh 2
    Vector3d(x, y, z)

  let trace (c: Coord) t =
    // runge kutta order two interpolation
    let cv = c.to_vector ()
    let v = get_interpolated_velocity cv.x cv.y cv.z
    let x = cv.x + 0.5 * t * v.x
    let y = cv.y + 0.5 * t * v.y
    let z = cv.z + 0.5 * t * v.z
    let dv = get_interpolated_velocity x y z
    let p = cv .+ (dv .* t)
    let to_int x = round x |> int
    { x = to_int p.x; y = to_int p.y; z = to_int p.z }

  let internal get_shared_velocity d n =
    match get n with
      | Some c when c.media = Fluid && Coord.is_bordering d c.velocity ->
        Coord.border d c.velocity
      | _ ->
        Vector3d()

  let laplacian (where: Coord) =
    let neighbors = where.neighbors ()
    let where_v = match get where with
                    | Some c -> c.velocity .* 6.0
                    | None -> Vector3d()
    let v = Seq.fold (fun accum (d, n) -> accum .+ get_shared_velocity d n) (Vector3d()) neighbors
    v .- where_v

  // for the divergence, we want to ignore velocity components between
  // fluid and solid cells
  let internal get_shared_velocity' v d n =
    match get n with
      | Some c when is_solid c && Coord.is_bordering d c.velocity ->
        let nv = Coord.border d c.velocity
        let cv = Coord.border d v
        let result = nv .- cv
        result.x + result.y + result.z
      | _ ->
        0.0

  let divergence (where: Coord) =
    let neighbors = where.forwardNeighbors ()
    let v = match get where with
              | Some c -> c.velocity
              | None -> Vector3d()
    Seq.fold (fun accum (d, n) -> accum + get_shared_velocity' v d n) 0.0 neighbors
