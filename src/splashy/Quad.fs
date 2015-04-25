﻿namespace splashy

open Vector

module Quad =
  type Quad = { x1: Vector3d;
                x2: Vector3d;
                x3: Vector3d;
                x4: Vector3d; }

  // check for coplanarity by calculating the volume
  let coplanarity q =
    let ab = q.x1 .- q.x2
    let ac = q.x1 .- q.x3
    let ad = q.x1 .- q.x4
    let f = Vector3d.dot ab (Vector3d.cross ac ad)
    abs f < 0.000001

  let raw q =
    [| q.x1.x; q.x1.y; q.x1.z; 1.0;
       q.x2.x; q.x2.y; q.x2.z; 1.0;
       q.x3.x; q.x3.y; q.x3.z; 1.0;
       q.x4.x; q.x4.y; q.x4.z; 1.0 |]