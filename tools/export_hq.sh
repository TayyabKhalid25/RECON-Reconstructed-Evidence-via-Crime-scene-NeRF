#!/bin/bash
set -e
set -x

export PATH="/home/wahaj/miniforge3/envs/recon/bin:$PATH"
source /home/wahaj/miniforge3/etc/profile.d/conda.sh || true
conda activate recon || true

echo "Starting High Quality Training and Export for Scene 1 (Marker)"
ns-train splatfacto \
  --data /home/wahaj/datasets/1 \
  --max-num-iterations 30000 \
  --pipeline.model.num-downscales 0 \
  --experiment-name 1-hq \
  --output-dir /home/wahaj/RECON-Reconstructed-Evidence-via-Crime-scene-NeRF/outputs \
  --viewer.quit-on-train-completion True

echo "Exporting Scene 1 (Marker)"
CONFIG_1=$(ls -td /home/wahaj/RECON-Reconstructed-Evidence-via-Crime-scene-NeRF/outputs/1-hq/splatfacto/*/config.yml | head -n 1)
ns-export gaussian-splat --load-config "$CONFIG_1" --output-dir /home/wahaj/RECON-Reconstructed-Evidence-via-Crime-scene-NeRF/outputs/1-hq-export

echo "Copying to Desktop"
mkdir -p ~/Desktop
cp /home/wahaj/RECON-Reconstructed-Evidence-via-Crime-scene-NeRF/outputs/1-hq-export/splat.ply ~/Desktop/scene1_with_marker_hq.ply || true

exit 0
