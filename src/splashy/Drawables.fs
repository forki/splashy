namespace splashy

open System.Linq

open OpenTK
open OpenTK.Graphics
open OpenTK.Graphics.OpenGL

open Constants
open Aabb
open Coord
open Grid
open Simulator

type IDrawable =
  abstract member prepare: int -> unit
  abstract member render: int -> unit
  abstract member destroy: unit -> unit

// encapsulate GL calls to draw the bounding area, markers, and cells.
module Drawables =

  let internal prepare_aabb (program: int) colorData (data: float []) =
    let vao =
      let array = GL.GenVertexArray()
      GL.BindVertexArray(array)
      array

    let vbo =
      let buffer = GL.GenBuffer()
      GL.BindBuffer(BufferTarget.ArrayBuffer, buffer)
      buffer
    let vertexPosition = GL.GetAttribLocation(program, "vertex_position")
    let n = sizeof<float>
    GL.BufferData(BufferTarget.ArrayBuffer, nativeint(data.Length * n), data, BufferUsageHint.StaticDraw)
    GL.EnableVertexAttribArray(vertexPosition)
    GL.VertexAttribPointer(vertexPosition, 4, VertexAttribPointerType.Double, false, n, 0)
    GL.BindAttribLocation(program, vertexPosition, "vertex_position")

    let normals =
      let buffer = GL.GenBuffer()
      GL.BindBuffer(BufferTarget.ArrayBuffer, buffer)
      buffer
    let vertexNormal = GL.GetAttribLocation(program, "vertex_normal")
    let n = sizeof<float32>
    GL.BufferData(BufferTarget.ArrayBuffer, nativeint(Aabb.normalData.Length * n), Aabb.normalData, BufferUsageHint.StaticDraw)
    GL.EnableVertexAttribArray(vertexNormal)
    GL.VertexAttribPointer(vertexNormal, 4, VertexAttribPointerType.Float, false, n, 0)
    GL.BindAttribLocation(program, vertexNormal, "vertex_normal")

    let color =
      let buffer = GL.GenBuffer()
      GL.BindBuffer(BufferTarget.ArrayBuffer, buffer)
      buffer
    let vertexColor = GL.GetAttribLocation(program, "vertex_color")
    let n = sizeof<float32>
    let colors = Seq.collect Enumerable.Repeat [ colorData, data.Length / 4 + 1 ] |> Seq.concat |> Array.ofSeq
    GL.BufferData(BufferTarget.ArrayBuffer, nativeint(data.Length * n), colors, BufferUsageHint.StaticDraw)
    GL.EnableVertexAttribArray(vertexColor)
    GL.VertexAttribPointer(vertexColor, 4, VertexAttribPointerType.Float, false, n, 0)
    GL.BindAttribLocation(program, vertexColor, "vertex_color")

    let indices =
      let buffer = GL.GenBuffer()
      GL.BindBuffer(BufferTarget.ElementArrayBuffer, buffer)
      buffer
    let n = sizeof<int>
    GL.BufferData(BufferTarget.ElementArrayBuffer, nativeint(Aabb.indicesData.Length * n), Aabb.indicesData, BufferUsageHint.StaticDraw)

    GL.BindVertexArray(0)
    GL.BindBuffer(BufferTarget.ArrayBuffer, 0)
    GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0)
    GL.DisableVertexAttribArray(vertexColor)
    GL.DisableVertexAttribArray(vertexNormal)
    GL.DisableVertexAttribArray(vertexPosition)
    GL.DeleteBuffer(normals)
    GL.DeleteBuffer(indices)
    GL.DeleteBuffer(color)
    GL.DeleteBuffer(vbo)

    vao

  type AreaBounds () =
    let mutable vao = 0
    let mutable position = Matrix4.Identity
    let color = [|1.0f; 1.0f; 1.0f; 0.1f|]
    interface IDrawable with
      member this.prepare p =
        vao <- prepare_aabb p color (Aabb.rawData Constants.bounds)
      member this.render location =
        GL.UniformMatrix4(location, false, &position)
        GL.BindVertexArray(vao)
        GL.DrawElements(BeginMode.Quads, indicesData.Length, DrawElementsType.UnsignedInt, 0)
        GL.BindVertexArray(0)
      member this.destroy () =
        GL.DeleteVertexArray(vao)

  type CellBounds (cell) =
    let mutable data = cell
    let mutable vao = 0
    let mutable transform = Vector3.Zero
    let fluid_color = [|0.2f; 0.2f; 0.8f; 0.7f|]
    let solid_color = [|0.64f; 0.16f; 0.16f; 0.5f|]
    let air_color = [|1.0f; 1.0f; 1.0f; 0.01f|]
    let l = Constants.h / 2.0
    let cell_bounds = { min_bounds = Vector.Vector3d(-l, -l, -l);
                        max_bounds = Vector.Vector3d( l,  l,  l) }
    interface IDrawable with
      member this.prepare p =
        let color = match cell.media with
                       | Fluid -> fluid_color
                       | Solid -> solid_color
                       | Air -> air_color
        let newVao = prepare_aabb p color (Aabb.rawData cell_bounds)
        vao <- newVao
      member this.render location =
        let mutable m = Matrix4.CreateTranslation(transform)
        GL.UniformMatrix4(location, false, &m)
        GL.BindVertexArray(vao)
        GL.DrawElements(BeginMode.Quads, indicesData.Length, DrawElementsType.UnsignedInt, 0)
        GL.BindVertexArray(0)
      member this.destroy () =
        GL.DeleteVertexArray(vao)
    member this.set_translation t = transform <- t
