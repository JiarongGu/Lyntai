# Lyntai.ImagePipeline — the example image pipeline

A host-run example of Lyntai's generation layer on a local ComfyUI: a setup image, variations of it over several
seeds, one image composed from a style reference, a subject reference and a pose reference plus words, a replay of
that compose from its recipe, and with `--optional` an inpaint and an upscale. Each step is one tracked API-format
workflow in `templates/`, driven through `ComfyUiProvider` with the request's typed fields and its inputs by role.
The design, the node mapping and the recipe format are `docs/generation.md` §12.

## What you need

- A running ComfyUI (0.36.0 was measured) with the models of your own, in its usual folders: an SDXL base checkpoint in
  `checkpoints/`; `ip-adapter_sdxl_vit-h.safetensors` and `ip-adapter-plus_sdxl_vit-h.safetensors` in `ipadapter/`;
  `CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors` in `clip_vision/`; `controlnet-openpose-sdxl-1.0.safetensors` in
  `controlnet/`. Nothing here downloads a model, and the sample never asks the server to.
- Two custom-node packs loaded: `ComfyUI_IPAdapter_plus` and `comfyui_controlnet_aux` (`DWPreprocessor`, whose two
  DWPose ONNX files — `yolox_l.onnx` is not used, the template runs it with no detector — must already be in the pack's
  `ckpts/`, or the node fetches them on first use). The compose step needs both; the others need neither.
- A GPU with about 12 GB. Run one step at a time; the sample does.

## Run it

```
LYNTAI_COMFYUI_URL=http://127.0.0.1:8188
LYNTAI_COMFYUI_SDXL_CHECKPOINT=sd_xl_base_1.0.safetensors
dotnet run --project samples/Lyntai.ImagePipeline -- --optional --out devtools/_image-pipeline/run
```

`--out` defaults to a timestamped folder under `devtools/_image-pipeline/` (gitignored). Two optional variables make
the recipes carry each model file's sha256: `LYNTAI_COMFYUI_MODELS` (the server's `models` folder) and
`LYNTAI_COMFYUI_DWPOSE_DIR` (the folder holding the DWPose files). A hash is computed once and cached in
`model-hashes.json` beside the outputs.

Every step writes `NN-name/image.png`, `NN-name/inputs/<role>.png` and `NN-name/recipe.json`; the compose also writes
`skeleton.png`, what the pose preprocessor drew. The run ends by replaying the compose from its recipe into
`08-compose/replay/` and exits 0 only when the decoded pixels match. It prints each step's seconds and the card's peak
memory from `nvidia-smi` when that is on the path.

## Make your own step

A template is a graph file plus an entry in `templates/manifest.json`: the provider's own option keys
(`prompt-path`, `seed-path`, `input-path:<role>`, …) mapped to graph paths, and `fields` for the numbers a request has no
typed field for (a denoise strength, an adapter weight). The roles a template accepts are the `input-path:<role>`
keys it maps: an input in any other role is refused before anything is uploaded.

## Content

Every prompt here is plain and safe for work and names no artist, studio, franchise or real person; the subjects are
objects, an animal and a wooden mannequin. No face-identity model is used, and the sample neither adds nor removes a
content filter: a local ComfyUI has none in the path.
