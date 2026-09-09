"""Copy the original embedded GLB image bytes with semantic names (no image edits)."""
import json
import struct
from pathlib import Path

root = Path(__file__).resolve().parent
raw = (root / 'RuneObelisk_Textured.glb').read_bytes()
if raw[:4] != b'glTF':
    raise ValueError('Expected binary glTF')
json_size = struct.unpack_from('<I', raw, 12)[0]
model = json.loads(raw[20:20 + json_size])
bin_header = 20 + json_size
bin_size, bin_type = struct.unpack_from('<II', raw, bin_header)
if bin_type != 0x004E4942:
    raise ValueError('Expected embedded BIN chunk')
binary = raw[bin_header + 8:bin_header + 8 + bin_size]
mat = model['materials'][0]
pbr = mat['pbrMetallicRoughness']
mapping = {
    'BaseColor': pbr['baseColorTexture']['index'],
    'MetallicRoughness': pbr['metallicRoughnessTexture']['index'],
    'Normal': mat['normalTexture']['index'],
    'Emission': mat['emissiveTexture']['index'],
}
dest = root / 'Exports' / 'SourceTextures'
dest.mkdir(parents=True, exist_ok=True)
manifest = {}
for semantic, texture_index in mapping.items():
    image = model['images'][model['textures'][texture_index]['source']]
    view = model['bufferViews'][image['bufferView']]
    suffix = '.jpg' if image['mimeType'] == 'image/jpeg' else '.png'
    path = dest / ('RuneObelisk_' + semantic + suffix)
    offset = view.get('byteOffset', 0)
    path.write_bytes(binary[offset:offset + view['byteLength']])
    manifest[semantic] = path.name
manifest['Unity_notes'] = {
    'BaseColor': 'URP/Lit Base Map, sRGB enabled.',
    'Normal': 'Import as Texture Type Normal map, assign to Normal Map.',
    'Emission': 'Optional URP Emission map, enable Emission; inspect intensity in Unity.',
    'MetallicRoughness': 'Original glTF packing: G = roughness, B = metallic. Do NOT directly assign this to a Unity metallic-smoothness map. First prototype can use Metallic 0 and Smoothness 0.25 without this map.',
    'fbx_import': 'Default FBX import was verified for geometry and material slots. Textures require explicit Unity material assignment; appearance in URP is not yet validated.'
}
(dest / 'texture_manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(json.dumps(manifest, indent=2))
