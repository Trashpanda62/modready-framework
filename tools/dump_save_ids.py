"""Dump the type-id table of a Bannerlord .sav file.

Format (from TaleWorlds.SaveSystem decompile, game 1.4.x):
  [int32 metaSize][metaData][raw-deflate GameData]
GameData.Write order: header, objectData[], containerData[], strings.
Header is an "archive": folders + entries; each object folder has a Basics
entry = SaveId + propertyCount + childStructCount; each container folder has
an Object entry = SaveId + containerType + elementCount.
SaveId encoding: tag 0 = TypeSaveId(int32), 1 = GenericSaveId, 2 = ContainerSaveId.
"""
import io, struct, sys, zlib, json
from collections import Counter

FOLDER_EXT = {0: "Root", 1: "Object", 2: "Struct", 3: "Container", 4: "Strings"}
ENTRY_BASICS = 8
ENTRY_OBJECT = 9
CONTAINER_TYPE = {0: "None", 1: "List", 2: "Dictionary", 3: "Array", 4: "Queue", 5: "CustomList", 6: "CustomReadOnlyList"}


def r_int(b):
    return struct.unpack("<i", b.read(4))[0]


def r_short(b):
    return struct.unpack("<h", b.read(2))[0]


def r_3byte(b):
    v = int.from_bytes(b.read(3), "little")
    return v - 0x1000000 if v >= 0x800000 else v


def read_save_id(b):
    tag = b.read(1)[0]
    if tag == 0:
        return ("T", r_int(b))
    if tag == 1:
        base = read_save_id(b)  # consumes tag 0 + int
        n = b.read(1)[0]
        args = tuple(read_save_id(b) for _ in range(n))
        return ("G", base, args)
    if tag == 2:
        ct = b.read(1)[0]
        key = read_save_id(b)
        val = read_save_id(b) if ct == 2 else None
        return ("C", ct, key, val)
    raise ValueError(f"bad saveid tag {tag} at {b.tell()}")


def sid_str(s):
    if s[0] == "T":
        return str(s[1])
    if s[0] == "G":
        return f"G({sid_str(s[1])})-({','.join(sid_str(a) for a in s[2])})"
    if s[0] == "C":
        inner = sid_str(s[2]) + ("," + sid_str(s[3]) if s[3] else "")
        return f"C({CONTAINER_TYPE[s[1]]})-({inner})"


def iter_type_ints(s):
    if s[0] == "T":
        yield s[1]
    elif s[0] == "G":
        yield from iter_type_ints(s[1])
        for a in s[2]:
            yield from iter_type_ints(a)
    elif s[0] == "C":
        yield from iter_type_ints(s[2])
        if s[3]:
            yield from iter_type_ints(s[3])


def parse_archive(data):
    b = io.BytesIO(data)
    nfold = r_int(b)
    folders = {}
    for _ in range(nfold):
        parent = r_3byte(b)
        gid = r_3byte(b)
        lid = r_3byte(b)
        ext = b.read(1)[0]
        folders[gid] = (lid, ext)
    nent = r_int(b)
    entries = []
    for _ in range(nent):
        fid = r_3byte(b)
        eid = r_3byte(b)
        ext = b.read(1)[0]
        ln = struct.unpack("<H", b.read(2))[0]
        entries.append((fid, eid, ext, b.read(ln)))
    return folders, entries


def main(path, mod_floor=200000):
    raw = open(path, "rb").read()
    meta_size = struct.unpack("<i", raw[:4])[0]
    meta = raw[4 : 4 + meta_size]
    body = zlib.decompress(raw[4 + meta_size :], -15)
    b = io.BytesIO(body)
    hlen = r_int(b)
    header = b.read(hlen)
    folders, entries = parse_archive(header)

    obj_ids = Counter()
    cont_ids = Counter()
    for fid, eid, ext, data in entries:
        if fid == -1:
            continue
        lid, fext = folders.get(fid, (None, None))
        eb = io.BytesIO(data)
        if fext == 1 and ext == ENTRY_BASICS:
            obj_ids[sid_str(read_save_id(eb))] += 1
        elif fext == 3 and ext == ENTRY_OBJECT:
            cont_ids[sid_str(read_save_id(eb))] += 1

    try:
        meta_json = json.loads(meta.decode("utf-8", "replace"))
        mods = meta_json.get("List", {})
        interesting = {k: v for k, v in mods.items() if "Modules" in k or "ButterLib" in k or "Option" in k}
    except Exception:
        interesting = {}

    print(f"== {path}")
    print(f"   objects: {sum(obj_ids.values())} distinct {len(obj_ids)}; containers: {sum(cont_ids.values())} distinct {len(cont_ids)}")
    mods_str = interesting.get("Modules", "")
    if mods_str:
        print(f"   modules: {mods_str[:400]}")

    def is_modrange(sid_string):
        return any(v >= mod_floor for v in nums(sid_string))

    def nums(s_):
        out, cur = [], ""
        for ch in s_:
            if ch.isdigit():
                cur += ch
            else:
                if cur:
                    out.append(int(cur))
                cur = ""
        if cur:
            out.append(int(cur))
        return out

    print("   -- mod-range object type ids --")
    for sid_string, n in sorted(obj_ids.items(), key=lambda x: -x[1]):
        if is_modrange(sid_string):
            print(f"      {sid_string}  x{n}")
    print("   -- mod-range container ids --")
    for sid_string, n in sorted(cont_ids.items(), key=lambda x: -x[1]):
        if is_modrange(sid_string):
            print(f"      {sid_string}  x{n}")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        try:
            main(p)
        except Exception as e:
            print(f"== {p} FAILED: {e}")
