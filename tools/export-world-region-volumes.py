"""Convert Dump_RespawnAndRechargeDetails output to the embedded Facility brushes.

Set RESPAWN_RECHARGE_PROBE_OUT and run TerminalGuidProbeTests.Dump_RespawnAndRechargeDetails
against the installed game, then pass that output and a destination JSON path here.
"""
import argparse
import json
import math
from pathlib import Path


def objects(text):
    decoder = json.JSONDecoder()
    index = 0
    while (start := text.find("\n{", index)) >= 0:
        obj, index = decoder.raw_decode(text, start + 1)
        yield obj


def vector(value, default=0):
    return [value.get(axis, default) for axis in "XYZ"]


def quaternion(rotator):
    # Unreal FRotator::Quaternion, matching CUE4Parse's FRotator.Quaternion().
    p, y, r = [math.radians(rotator.get(axis, 0)) / 2 for axis in ("Pitch", "Yaw", "Roll")]
    sp, cp, sy, cy, sr, cr = math.sin(p), math.cos(p), math.sin(y), math.cos(y), math.sin(r), math.cos(r)
    return [cr * sp * sy - sr * cp * cy, -cr * sp * cy - sr * cp * sy,
            cr * cp * sy - sr * sp * cy, cr * cp * cy + sr * sp * sy]


def export(text):
    data = list(objects(text))
    result = []
    for actor in data:
        if actor.get("Type") != "AbioticLevelStreamingVolume":
            continue
        props = actor["Properties"]
        region = props["LevelToLoad"]["AssetPathName"].rsplit(".", 1)[-1]
        children = [o for o in data if o.get("Outer", {}).get("ObjectName", "").endswith("." + actor["Name"] + "'")]
        component = next(o for o in children if o["Type"] == "BrushComponent")
        model = next(o for o in children if o["Type"] == "Model")
        transform = component["Properties"]
        assert all(n["NodeFlags"] == 0 for n in model["Nodes"]), "Non-CSG BSP flags need explicit handling"
        result.append(dict(Region=region, Actor=actor["Name"], Origin=vector(transform.get("RelativeLocation", {})),
            Rotation=quaternion(transform.get("RelativeRotation", {})), Scale=vector(transform.get("RelativeScale3D", {}), 1),
            BoundsOrigin=vector(model["Bounds"]["Origin"]), Extent=vector(model["Bounds"]["BoxExtent"]),
            Nodes=[[n["Plane"][axis] for axis in "XYZ"] + [n["Plane"]["W"], n["iFront"], n["iBack"]] for n in model["Nodes"]]))
    if not result:
        raise ValueError("No streaming brushes found")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    args.destination.write_text(json.dumps(export(args.source.read_text(encoding="utf-8")), separators=(",", ":")) + "\n", encoding="utf-8")
