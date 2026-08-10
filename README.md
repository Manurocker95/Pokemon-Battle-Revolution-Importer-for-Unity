# Pokémon Battle Revolution — OBM / SDR / OUT Model & Animation Reverse-Engineering Notes

> **Basis:** the supplied working `PS3DS_PokemonOBMImporter.cs`.
>
> **Important scope note:** unlike the Stadium 1/2 write-ups, this importer does **not** currently extract Pokémon directly from the Wii disc/container formats. Its input pipeline starts from already-extracted **OBM**, **SDR/OUT**, and **TGA** files. Therefore this document describes the structures and relationships that the importer has actually established in those files. It deliberately does not invent disc archive offsets, container names, compression formats, or undocumented fields that are not supported by the supplied code.

## 1. High-level asset pipeline

The working reconstruction pipeline is:

```text
Pokémon Battle Revolution extracted assets
    |
    +-- *.obm
    |    +-- meshes
    |    +-- positions
    |    +-- normals
    |    +-- UVs
    |    +-- material groups
    |    +-- texture names
    |    +-- optional skeleton
    |    `-- optional skin weights
    |
    +-- *.sdr / *.out
    |    +-- skeleton(s)
    |    +-- hierarchy
    |    +-- bind transforms
    |    +-- inverse bind matrices
    |    +-- skin weights
    |    +-- named actions
    |    `-- F-curves / keyframes
    |
    `-- *.tga
         `-- textures
```

The practical lesson is that the model reconstruction is **multi-file**.

OBM is sufficient for visible mesh topology/material grouping and may contain skeleton/weight information, but SDR/OUT is an important second source for:

* skeleton recovery;
* skinning recovery;
* bind information;
* animations.

The importer explicitly falls back to SDR when the OBM does not contain a usable skeleton or skin table.

---

# Part I — OBM

## 2. OBM is text-based in the supplied pipeline

The importer reads the entire OBM using:

```csharp
File.ReadAllLines(path)
```

and tokenizes individual commands.

So the `.obm` handled here is not being interpreted as an opaque binary container. It is a textual model description with commands conceptually similar to an extended OBJ.

Comments begin with:

```text
#
```

Blank lines and comments are ignored.

---

## 3. OBM command vocabulary

The working parser recognizes the following commands.

### `o` — mesh/object

```text
o <name>
```

Starts a new mesh.

Internal representation:

```text
PS3DS_OBMMesh
```

A single OBM may contain multiple mesh objects.

---

### `v` — vertex position

```text
v <x> <y> <z>
```

Stored directly as a 3D position.

---

### `vn` — vertex normal

```text
vn <x> <y> <z>
```

Stored as a normal vector.

---

### `vt` — texture coordinate

```text
vt <u> <v>
```

The importer optionally performs:

```text
v = 1 - v
```

when `Flip V` is enabled.

That flip is an exporter/engine-coordinate option, not evidence that the source file itself stores inverted UVs.

---

## 4. OBM skin weights: `vw`

The importer has encountered several real-world textual encodings of `vw`.

Supported forms are:

```text
vw bone/weight bone/weight
vw bone weight bone weight

vw vertexIndex bone/weight bone/weight
vw vertexIndex bone weight bone weight
```

This matters because a simplistic parser that only accepts `bone/weight` pairs can silently produce empty skin rows and make the whole Pokémon appear attached to bone 0.

### 4.1 Explicit vs implicit vertex index

If the line begins with a plausible source vertex index, that index is used directly.

Otherwise, each `vw` line is appended as the next weight row.

Conceptually:

```text
weights[sourceVertex] = [
    (boneIndex, weight),
    ...
]
```

### 4.2 Weight normalization

The importer is deliberately permissive.

Weights may appear normalized already:

```text
0.0 .. 1.0
```

or in integer-like ranges.

Values greater than `1` are interpreted approximately as:

```text
<= 100      -> percentage / 100
<= 255      -> byte / 255
otherwise   -> ushort / 65535
```

Only positive weights are retained.

Before conversion to Unity `BoneWeight`, weights are sorted and the strongest four influences are used.

This normalization policy is importer behavior; it should not be taken as proof that every PBR exporter/source uses all of those encodings.

---

## 5. Material groups

### `g`

```text
g <material/group name>
```

Begins a material group.

### `fc`

```text
fc <r> <g> <b> <a>
```

The working parser treats these as 0–255 color components:

```text
Color(r/255, g/255, b/255, a/255)
```

### `ft`

```text
ft <texture name>
```

Adds a texture name to the current material group.

A material group may therefore reference more than one texture name.

---

## 6. Faces

Faces are read from:

```text
f ...
```

The importer expects triangular faces and reads three vertex specifications.

Each vertex token is split as:

```text
v/n/t/w
```

and interpreted as:

```text
V = position index
N = normal index
T = UV index
W = weight-row index
```

Missing components become `-1` where applicable.

An important practical observation in the supplied code is that some PBR OBM dumps contain faces such as:

```text
v/n/t/0
```

even when the real skin table is effectively one row per source vertex.

For this reason, the importer contains fallback logic around source-vertex and weight-row association instead of blindly trusting `W`.

---

# Part II — OBM skeleton data

## 7. Armature and bones

The textual skeleton commands recognized by the importer are:

```text
r
rb
rx
rp
rq
rm
```

### `r` — armature

```text
r <armature name>
```

Sets the armature/root name.

### `rb` — new bone

```text
rb <bone name>
```

Starts a bone record.

Default values are:

```text
parent   = none
position = (0,0,0)
rotation = identity quaternion
scale    = (1,1,1)
```

### `rx` — parent

```text
rx <parent bone name>
```

The hierarchy is name-based in the OBM parser.

### `rp` — position

```text
rp <x> <y> <z>
```

### `rq` — quaternion

```text
rq <x> <y> <z> <w>
```

### `rm` — 3x3 matrix

```text
rm <9 floats>
```

The importer preserves the matrix as nine floats.

The supplied reconstruction primarily uses the explicit position/quaternion/scale representation when creating Unity bones.

---

## 8. OBM skeleton fallback behavior

A significant finding from the working importer is that **the OBM skeleton cannot always be treated as complete**.

The import sequence is:

```text
Parse OBM

if OBM has no bones and SDR has bones:
    build OBM-compatible bone list from SDR

if SDR has usable skin weights:
    use them when OBM lacks usable weights
```

This makes SDR/OUT part of the effective model format reconstruction rather than merely "an animation file."

---

# Part III — SDR / OUT

## 9. Binary properties

Unlike the textual OBM, SDR/OUT is parsed as a binary big-endian structure.

The reader implements:

```text
u8
s8
u16 BE
s16 BE
u32 BE
float BE
strings by address
```

All addresses used by the current parser are treated as offsets into the loaded SDR/OUT byte array.

The importer accepts either `.sdr` or `.out` for this path.

---

## 10. SDR top-level header

The parser begins with:

```text
+0x08 : u32 skeleton list address
+0x18 : u16 number of skeletons
```

If either is zero, the file is rejected as not looking like a normal SDR with a skeleton list.

The skeleton list is an array of 32-bit addresses:

```text
skeletonHeader = *(skeletonList + i * 4)
```

---

## 11. Skeleton header

For each skeleton header, the importer currently uses:

```text
+0x00 : u32 name address
+0x08 : u16 number of actions
+0x0C : u32 actions address
+0x10 : u32 root bone/node address
```

The name is a string referenced by address.

Actions are parsed before recursively walking the node hierarchy.

---

## 12. Action table

Each action record is treated as:

```text
stride = 0x30 bytes
```

The parser currently requires only:

```text
+0x00 : u32 action name address
```

The action index in this table becomes the key used later by animation/F-curve blocks.

If a name is empty, the importer falls back to:

```text
Action_<index>
```

Not every field in the 0x30-byte action record has been semantically identified by the current importer.

---

# Part IV — SDR node hierarchy

## 13. Node/bone structure

The recursive node parser currently reads:

```text
+0x00 : u32 node type
+0x04 : u32 name address
+0x08 : u16 bone/node index

+0x0C : u32 position Vec3 address
+0x10 : u32 rotation Euler Vec3 address
+0x14 : u32 scale Vec3 address

+0x20 : u32 animation-data / F-curve-block address

+0x24 : u32 child address
+0x28 : u32 next-sibling address
```

The hierarchy is therefore represented as a child/sibling tree.

Traversal is:

```text
parse current
    |
    +-- recurse child with current index as parent
    |
    `-- recurse next sibling with current parent
```

This is one of the most useful structural observations for anyone implementing an independent reader.

---

## 14. Node types observed by the importer

The code has special handling for at least:

```text
nodeType == 2
nodeType == 3
```

The exact original symbolic names are not asserted here.

### Type 2

For node type 2:

```text
+0x34 : float bind rotation X
+0x38 : float bind rotation Y
+0x3C : float bind rotation Z

+0x44 : 3x4 inverse bind matrix
```

The matrix is 12 big-endian floats:

```text
3 rows x 4 columns
```

and the importer expands it into a conventional 4x4 matrix with:

```text
last row = 0,0,0,1
```

### Type 3

For node type 3:

```text
+0x30 : u32 mesh address
```

The mesh structure is then inspected for skin-weight data.

It is safest to call these "node type 2" and "node type 3" until their original engine names are independently confirmed.

---

# Part V — SDR skinning

## 15. Mesh skin-weight reference

For a type-3 node's mesh structure, the importer uses:

```text
mesh + 0x02 : u16 vertex count
mesh + 0x0C : u32 weights structure address
```

The decoded weight-row count is compared with this vertex count as a consistency check.

---

## 16. Weight structure

The current parser has identified three weight sections.

### 16.1 Single-bone groups

Header:

```text
+0x00 : u16 number of one-bone groups
+0x04 : u32 one-bone group data address
```

Each group begins with:

```text
u16 number of vertices
u16 bone index
```

For every vertex in the group:

```text
weight = 1.0
```

---

### 16.2 Two-bone groups

Header:

```text
+0x08 : u16 number of two-bone groups
+0x0C : u32 two-bone group data address
+0x10 : u32 two-bone weight values address
```

Each group record is six bytes:

```text
+0x00 : u16 number of vertices
+0x02 : u16 bone 1
+0x04 : u16 bone 2
```

Each vertex consumes one `u16` from the weight-value stream:

```text
w1 = raw / 65535
w2 = 1 - w1
```

This is a compact representation for runs of vertices influenced by the same pair of bones.

---

### 16.3 Extra weights

Header:

```text
+0x14 : u16 number of extra-weight records
+0x18 : u32 extra-weight data address
```

Each record is ten bytes:

```text
+0x00 : u16 vertex index
+0x02 : u16 bone 1
+0x04 : u16 bone 2
+0x06 : u16 raw weight 1
+0x08 : u16 raw weight 2
```

`bone2 == 0xFFFF` is treated as absent.

The existing weights on that vertex are first scaled by:

```text
remaining = clamp01(1 - w1 - w2)
```

then the new influences are added and the row is normalized.

This layered representation is an important PBR-specific skinning detail exposed by the current SDR parser.

---

# Part VI — SDR animation

## 17. Per-node animation block chain

Each node may point to animation data at:

```text
node + 0x20
```

The parser treats this as a linked list of F-curve blocks.

Each block uses:

```text
+0x00 : u16 action index
+0x02 : u16 number of F-curves
+0x04 : u32 F-curve list address
+0x0C : u32 next block address
```

The implementation guards against malformed loops with a maximum traversal count.

Each block associates a set of curves for the **current bone/node name** with a particular action.

Conceptually:

```text
Action
  `-- BoneName
       `-- [FCurve, FCurve, ...]
```

---

## 18. F-curve descriptor

Each F-curve record has a stride of:

```text
0x10 bytes
```

Fields currently interpreted are:

```text
+0x01 : u8 component index
+0x02 : u8 axis
+0x03 : u8 channel index

+0x06 : u8 data type ID
+0x07 : u8 scale exponent

+0x08 : u32 keyframe-data address
```

### 18.1 Components

The working mapping is:

```text
0 = Location
1 = RotationEuler
2 = Scale
```

Curves with component values outside this range are ignored.

### 18.2 Axis behavior

When:

```text
axis == 0
```

the importer treats the curve as a `Vec3`.

Otherwise the axis is generally converted from 1-based to 0-based:

```text
axis - 1
```

with channel index used as a fallback when needed.

---

## 19. SDR value types

The current reader recognizes:

```text
Float
Quat
UChar
Char
UShort
Short
Vec2
Vec3
```

For native float/vector/quaternion types, the scale exponent is disabled by the importer.

For packed integer representations, values may be divided by:

```text
2 ^ scaleExponent
```

This allows the format to represent compact fixed-point-like animation data.

---

# Part VII — keyframe data

## 20. Keyframe-data structure

Given the address from an F-curve descriptor, the importer currently reads:

```text
+0x00 : u32 values address
+0x08 : u16 value count

+0x10 : u32 keyframe records address
+0x14 : u16 number of keyframes
+0x16 : u16 field whose low byte is used as frame rate
```

Two modes are supported.

---

## 21. Explicit keyframe mode

When:

```text
numKeyframes > 0
```

each keyframe record is:

```text
stride = 0x0C

+0x02 : u16 value index
+0x08 : float time
```

The value index selects an entry from the value table.

The resulting key is:

```text
(time, decodedValue / 2^exp)
```

The current importer does not claim semantic names for the unused bytes in the 0x0C record.

---

## 22. Sampled/value-array mode

When no explicit keyframe records exist but:

```text
valueCount > 0
```

the parser treats the values as regularly sampled animation data.

Frame rate is obtained from the low byte at:

```text
keyframeData + 0x16
```

If this is zero, the importer falls back to its configured SDR frame rate, and finally to 30 FPS.

This is important because not every animation curve needs an explicit time record per sample.

---

# Part VIII — transforms and coordinate conversion

## 23. Rotation source

SDR rotation curves are treated as **Euler angles in radians**.

For Unity export:

```text
radians * Rad2Deg
```

is used where Euler curves are written directly.

The importer also supports constructing quaternions from the three Euler components.

---

## 24. Euler order is not safe to assume

The importer exposes several possible Euler orders:

```text
XYZ
XZY
YXZ
YZX
ZXY
ZYX
```

This exists because a superficially correct animation can still twist incorrectly if the multiplication order is wrong.

A reverse-engineering implementation should preserve the raw Euler components and make the order explicit rather than prematurely baking them under an assumed convention.

---

## 25. Axis correction

The supplied importer contains configurable SDR axis corrections such as:

```text
X +/-90
Y +/-90
Z +/-90
X-90 + Y+/-90
```

and independent X/Y/Z rotation inversion options.

These are **conversion/debugging controls**, not proven fields in SDR.

Their existence is useful evidence that engine coordinate-system conversion must be kept separate from binary decoding.

---

## 26. Animation value modes

The importer supports multiple interpretations when transferring SDR transforms into the reconstructed skeleton, including:

```text
raw/absolute-style use
relative to rest pose
relative to first frame
```

This is another exporter-level concern.

For reverse engineering, preserve:

* source bind transform;
* source animation values;
* source key times;

before applying engine-specific rest-pose corrections.

---

# Part IX — reconstructing the model

## 27. Recommended merge strategy

The supplied importer effectively follows this decision tree:

```text
Parse OBM
Parse matching SDR/OUT if available

Skeleton:
    use OBM bones if present
    otherwise construct bones from SDR

Skin weights:
    use valid OBM VW rows if present
    otherwise use SDR MeshWeights when counts match

Geometry:
    OBM

Materials / texture references:
    OBM

Texture images:
    TGA

Animations:
    SDR/OUT
```

This is a good architectural model for an independent exporter.

---

## 28. Matching SDR/OUT to OBM

The importer is designed around corresponding external files and can automatically search for a matching SDR/OUT when importing an OBM.

That association is currently filename/workflow based rather than discovered through a Wii disc-level resource table in this code.

Therefore:

**do not infer from this importer that OBM itself contains a pointer to its SDR.**

The supplied implementation does not demonstrate that.

---

# Part X — textures

## 29. Texture source

Material groups in OBM contain texture names through `ft`.

The importer searches a user-selected TGA folder for those names.

It can:

* copy TGA files into the Unity project;
* optionally convert them to PNG;
* create Unity materials referencing the imported texture.

There is also support for a separate shiny TGA folder.

Again, this write-up can confidently document the OBM -> texture-name relationship, but not the original disc texture container because that extraction stage is outside the supplied importer.

---

# Part XI — diagnostic lessons learned

## 30. "Everything is attached to bone 0"

This was explicitly anticipated by the importer.

Likely causes include:

```text
VW rows failed to parse
face W indices do not actually identify the intended rows
OBM stores one VW row per source vertex
weights use a syntax the parser did not recognize
```

The importer therefore logs:

* number of source weight rows;
* number of non-empty rows;
* generated Unity vertices;
* vertices influenced by non-root bones;
* maximum referenced bone index;
* raw VW samples.

For reverse engineering, this is a better diagnostic than simply looking at the deformed model.

---

## 31. SDR weight count vs OBM vertex count

When applying SDR skin weights to OBM geometry, the current implementation requires a compatible row count.

If:

```text
SDR MeshWeights.Count == OBM mesh.Vertices.Count
```

the weights can be cloned directly.

Otherwise the importer warns instead of guessing.

This is the correct conservative behavior until a more explicit vertex-remapping relationship is proven.

---

## 32. Bind matrices vs displayed bone transforms

SDR may provide:

* position pointer;
* Euler rotation pointer;
* scale pointer;
* additional bind Euler data for node type 2;
* inverse bind matrix.

These should not be collapsed into one concept.

A renderer needs a coherent rest hierarchy and bindposes, but a reverse-engineering tool should retain all source fields independently until their exact runtime roles are understood.

---

# Part XII — what is confirmed vs provisional

## 33. Strongly established by the supplied working importer

The code directly supports the following claims:

* OBM is parsed as a text command format;
* multiple meshes can exist in one OBM;
* `v`, `vn`, `vt`, `vw`, `g`, `fc`, `ft`, `f`, `r`, `rb`, `rx`, `rp`, `rq`, and `rm` are meaningful commands to this format/export;
* face tokens contain position/normal/UV/weight-style indices;
* OBM may provide skeleton and skin weights;
* SDR/OUT is big-endian binary;
* SDR top-level skeleton list is referenced at `+0x08`;
* skeleton count is read at `+0x18`;
* skeleton headers reference names, actions, and a root node;
* the node hierarchy is child/sibling linked;
* node records reference position, Euler rotation, scale, animation data, children, and siblings;
* node type 2 contains additional bind rotation and a 3x4 inverse bind matrix;
* node type 3 can reference mesh/skin information;
* SDR contains compact one-bone and two-bone weight groups plus extra per-vertex influences;
* SDR animation is organized by actions, bones, F-curves, and keyframe/value tables;
* Location, Euler Rotation, and Scale components are decoded;
* several scalar/vector integer and floating data types occur;
* curves can use explicit keyframe times or regular sampled values;
* rotations are interpreted as radians by the current importer;
* SDR can recover skeleton/weights when OBM information is absent or unusable.

---

## 34. Things this importer does **not** establish

Do not use this source alone to claim:

* the original Wii disc archive/container offsets;
* the disc compression algorithm;
* that OBM/SDR/TGA filenames are stored together in one original archive;
* original internal symbolic names for SDR node types;
* semantics of every unused SDR header/action/node field;
* that every PBR Pokémon uses exactly the same subset of structures;
* the original renderer/shader pipeline;
* the exact original shiny-generation algorithm;
* that Unity-specific axis/Euler corrections represent native game behavior.

Those require additional disc-level or executable-level reverse engineering.

---

# Part XIII — minimal pseudocode

## 35. OBM

```c
for line in read_lines(obm):
    cmd = tokenize(line)

    switch cmd:
        case "o":
            currentMesh = new Mesh()

        case "v":
            currentMesh.positions += vec3(...)

        case "vn":
            currentMesh.normals += vec3(...)

        case "vt":
            currentMesh.uvs += vec2(...)

        case "vw":
            parse_weight_row(...)

        case "g":
            currentMaterialGroup = new MaterialGroup()

        case "fc":
            currentMaterialGroup.color = rgba8(...)

        case "ft":
            currentMaterialGroup.textures += textureName

        case "f":
            currentMesh.faces += parse_v_n_t_w_triangle(...)

        case "r":
            armatureName = ...

        case "rb":
            currentBone = new Bone(...)

        case "rx":
            currentBone.parentName = ...

        case "rp":
            currentBone.position = ...

        case "rq":
            currentBone.quaternion = ...

        case "rm":
            currentBone.matrix3x3 = ...
```

---

## 36. SDR

```c
bytes = read_big_endian_file(sdr)

skeletonList = u32(bytes + 0x08)
skeletonCount = u16(bytes + 0x18)

for i in 0 .. skeletonCount-1:
    skeleton = u32(skeletonList + i*4)

    name       = string(u32(skeleton + 0x00))
    actionCount= u16(skeleton + 0x08)
    actions    = u32(skeleton + 0x0C)
    rootNode   = u32(skeleton + 0x10)

    parse_actions(actions, actionCount)
    parse_node_recursive(rootNode)
```

Node traversal:

```c
parse_node(node, parent):
    type  = u32(node + 0x00)
    name  = string(u32(node + 0x04))
    index = u16(node + 0x08)

    position = optional_vec3(u32(node + 0x0C))
    rotation = optional_vec3(u32(node + 0x10))
    scale    = optional_vec3(u32(node + 0x14))

    anim = u32(node + 0x20)
    child = u32(node + 0x24)
    next  = u32(node + 0x28)

    if type == 2:
        bindEuler = vec3(node + 0x34)
        inverseBind = matrix3x4(node + 0x44)

    if type == 3:
        mesh = u32(node + 0x30)
        parse_weights(mesh)

    parse_fcurve_blocks(anim, name)

    parse_node(child, index)
    parse_node(next, parent)
```

---

# Part XIV — comparison with the N64 Stadium games

## 37. Conceptual difference

The N64 Stadium games expose a low-level console-native model representation:

```text
FRAGMENT
N64 display lists
TLUTs
packed animation descriptors
```

The PBR workflow represented by this importer is structurally very different:

```text
OBM textual geometry/material description
+
SDR/OUT binary skeleton/animation/skinning data
+
external TGA textures
```

The recurring conceptual pieces remain familiar:

```text
mesh
skeleton
bind pose
skin weights
animation channels
materials/textures
```

but their storage is no longer analogous to the N64 FRAGMENT pipeline.

In particular, do not try to force PBR into the Stadium 1/2 model:

```text
"bone channel N -> packed S/R/T table"
```

SDR instead exposes named/node-associated **F-curves** grouped by action.

---

# Part XV — useful next reverse-engineering targets

The supplied importer is already enough to reconstruct a substantial part of the asset, but a deeper PBR investigation would benefit from:

1. identifying the original disc containers from which OBM/SDR/TGA were extracted;
2. documenting filename/resource association at disc level;
3. naming SDR node types from executable symbols or SDK-derived structures;
4. mapping the currently-unused fields in the 0x30-byte action records;
5. mapping all bytes in the 0x10-byte F-curve descriptors;
6. identifying interpolation/tangent metadata rather than treating curves only through the currently required values;
7. confirming the exact role of bind Euler values vs position/rotation pointers and inverse bind matrices;
8. proving vertex correspondence when SDR weight rows and OBM source vertices do not trivially match;
9. documenting PBR material animation / texture animation if present outside the current skeleton-animation path;
10. reconstructing the original Wii rendering/material state rather than approximating it in Unity.

These are good boundaries between **what the current importer has demonstrated** and the next layer of reverse engineering.

---

# 38. Practical takeaway

For a developer writing a PBR Pokémon importer from the same extracted inputs, the shortest accurate model is:

```text
OBM = visible model description
      + sometimes skeleton/weights

SDR/OUT = authoritative recovery source for
          skeleton hierarchy
          bind information
          skinning
          named actions
          transform F-curves

TGA = image data referenced by OBM material groups
```

The critical implementation rule is to avoid treating any one of these files as necessarily self-contained.

A robust importer should parse them independently, preserve source indices/names, and merge them only after validating skeleton, vertex and weight relationships.
