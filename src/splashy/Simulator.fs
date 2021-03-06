namespace Splashy

open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols
open System
open OpenTK

open Constants
open Cell
open Vector
open Aabb
open Coord
open Grid
open Build
open Convection
open Viscosity
open Pressure
open Forces

module Simulator =

  let mutable private markers: Coord list = []         // closest coordinate locations.
  let mutable private locations: Vector3d<m> list = [] // real locations.

  let get_velocity (m: Coord) =
    let c = Grid.raw_get m
    let v = m.backward_neighbors ()
            |> Seq.fold (fun accum (d, n) ->
                           match Grid.get n with
                            | Some nc ->
                              let nv = Coord.border d nc.velocity
                              accum .+ nv
                            | None ->
                              accum
                         ) Vector3d.ZERO
    v .+ c.velocity

  // for now, advance by frame.
  let move_markers dt =
    locations <- Seq.map (fun (l: Vector3d<m>) ->
                            let m = Coord.construct(l.x, l.y, l.z)
                            let v = get_velocity m
                            l .+ (v .* dt)
                          ) locations |> Seq.toList
    let moved = Seq.fold (fun accum (m: Coord, l: Vector3d<m>) ->
                            let c = Coord.construct(l.x, l.y, l.z)
                            if m <> c then
                              (m, c) :: accum
                            else
                              accum
                         ) [] (Seq.zip markers locations)
    markers <- Seq.map (fun (l: Vector3d<m>) -> Coord.construct(l.x, l.y, l.z)) locations |> Seq.toList
    moved

  let advance dt sanity =
    let dt = dt * 1.0<s> // * Constants.time_step
    printfn "-->"
    printfn "Moving simulation forward with time step %A." dt

    printfn "Moving fluid markers."
    move_markers dt |> Grid.move_cells

    if sanity then
      printfn "* Verifying containment."
      Build.check_containment markers

    Build.setup (fun () ->
      printfn "  Setup: Adding possible new fluid markers."
      Build.add_new_markers markers |> Grid.add_cells
      printfn "  Setup: Setting fluid layers."
      Build.set_fluid_layers markers
      printfn "  Setup: Creating air buffer."
      Build.create_air_buffer () |> Grid.add_cells
      printfn "  Setup: Removing unused layers."
      Build.delete_unused () |> Grid.delete_cells
    )

    if sanity then
      printfn "* Verifying marker surroundings."
      Build.check_surroundings markers

    printfn "Applying convection term -(∇⋅u)u."
    Convection.apply markers dt |> Grid.update_velocities
    printfn "Applying external forces term F."
    Forces.apply markers dt |> Grid.update_velocities
    printfn "Applying viscosity term v∇²u."
    Viscosity.apply markers dt |> Grid.update_velocities
    printfn "Applying pressure term -1/ρ∇p."
    Pressure.calculate markers dt |> Grid.update_pressures
    Pressure.apply markers dt |> Grid.update_velocities

    if sanity then
      printfn "* Verifying pressures."
      Pressure.check_pressures markers

    if sanity then
      printfn "* Verifying divergence (2)."
      Pressure.check_divergence markers

    printfn "Cleaning up fluid velocities."
    Build.cleanup (fun () ->
      printfn "  Cleanup: Propagating fluid velocities into surroundings."
      Build.propagate_velocities () |> Grid.update_velocities
      printfn "  Cleanup: Setting velocities pointing into solid cells to zero."
      Build.zero_solid_velocities() |> Grid.update_velocities
      // for now...
      printfn "  Cleanup: Set solid velocities to zero."
      Grid.filter Cell.media_is_solid
      |> Seq.map (fun (solid: Coord) -> (solid, Vector3d.ZERO))
      |> Grid.update_velocities
    )

  // generate a random amount of markers to begin with (testing purposes only).
  let generate n =
    let seed = 12349
    let r = System.Random(seed)
    let h = int Constants.h
    let l = int (Constants.world_h / float32 Constants.h)
    let new_markers = [ for _ in 0..n-1 do
                        let x = r.Next(-l, l + 1) * h
                        let y = r.Next(-l, l + 1) * h
                        let z = r.Next(-l, l + 1) * h
                        yield Coord.construct(x, y, z) ]
    markers <- Set.ofList new_markers |> Seq.toList
    let to_vec m = Vector3d<m>(float m.x * 1.0<m>, float m.y * 1.0<m>, float m.z * 1.0<m>)
    locations <- Seq.map to_vec markers |> Seq.toList
    let actual = Seq.length markers
    Build.setup (fun () -> Build.add_new_markers markers |> Grid.add_cells)
    if actual <> n then
      printfn "Warning: could only generate %d random markers." actual
