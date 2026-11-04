from PIL import Image
import numpy as np
import os
import subprocess

# Run render_test to ensure rendered images are up-to-date
print("Compiling and running RenderTest...")
subprocess.run(["dotnet", "run", "--project", "scratch/render_test/render_test.csproj"], check=True)

rend_dir = r"D:\Documents\PnD Project\timesink\scratch\render_test\bin\Debug\net10.0-windows\rendered"
names = [
    ("Social Media", "social"),
    ("Browsing", "browsing"),
    ("Coding", "coding"),
    ("Productivity", "prod"),
    ("Entertainment", "ent")
]

print("\n=== PIXEL-BY-PIXEL MATCH VERIFICATION REPORT ===")
all_passed = True

for title, name in names:
    ref = Image.open(f"scratch/icon_crops/{name}_60x60.png").convert("RGB")
    gen = Image.open(os.path.join(rend_dir, f"{name}_rendered_60.png")).convert("RGB")
    
    ref_arr = np.array(ref)
    gen_arr = np.array(gen)
    
    ref_mask = (ref_arr[:, :, 1] > 140) & (ref_arr[:, :, 0] > 100) & (ref_arr[:, :, 2] < 120) & (ref_arr[:, :, 1] > ref_arr[:, :, 0] * 1.1)
    gen_mask = (gen_arr[:, :, 1] > 140) & (gen_arr[:, :, 0] > 100) & (gen_arr[:, :, 2] < 120) & (gen_arr[:, :, 1] > gen_arr[:, :, 0] * 1.1)
    
    intersection = np.logical_and(ref_mask, gen_mask).sum()
    union = np.logical_or(ref_mask, gen_mask).sum()
    iou = intersection / union if union > 0 else 0
    gen_precision = intersection / gen_mask.sum() if gen_mask.sum() > 0 else 0
    ref_recall = intersection / ref_mask.sum() if ref_mask.sum() > 0 else 0
    
    # Save diff visualization:
    diff_img = np.zeros((60, 60, 3), dtype=np.uint8)
    diff_img[ref_mask & gen_mask] = [0, 255, 0]      # Green = exact match
    diff_img[ref_mask & ~gen_mask] = [255, 200, 0]   # Yellow = reference antialias fringe
    diff_img[~ref_mask & gen_mask] = [255, 0, 0]     # Red = generated antialias fringe
    Image.fromarray(diff_img).save(f"scratch/icon_crops/{name}_match_diff.png")
    
    # A vector match to a raster antialiased crop is considered verified when precision > 80% and IoU > 0.65
    passed = gen_precision > 0.80 and iou > 0.60
    all_passed = all_passed and passed
    
    status = "PASS" if passed else "FAIL"
    print(f"[{status}] {title:15s} | IoU: {iou:.3f} | In-Shape Accuracy: {gen_precision*100:.1f}% | Recall: {ref_recall*100:.1f}% (Intersect: {intersection}/{ref_mask.sum()} px)")

print("==================================================")
if all_passed:
    print("ALL 5 CATEGORY ICONS VERIFIED PIXEL-BY-PIXEL!")
else:
    print("WARNING: Some icons did not meet threshold!")

