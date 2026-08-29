/**
 * 将 /res 下的 SVG 图标渲染为 Android 启动器图标资源。
 * 运行前需安装 sharp：
 *   npm install sharp
 * 然后（以项目根目录为工作目录）：
 *   node res/generate-android-icons.js
 */
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');

const root = path.resolve(__dirname, '..');
const androidRes = path.join(root, 'src', 'MediaOrganizer.Android', 'Resources');
const bgSvg = path.join(root, 'res', 'icon-android-background.svg');
const fgSvg = path.join(root, 'res', 'icon-android-foreground.svg');

const densities = [
  { name: 'mdpi', dp: 108, scale: 1 },
  { name: 'hdpi', dp: 108, scale: 1.5 },
  { name: 'xhdpi', dp: 108, scale: 2 },
  { name: 'xxhdpi', dp: 108, scale: 3 },
  { name: 'xxxhdpi', dp: 108, scale: 4 },
];

const legacyDensities = [
  { name: 'mdpi', size: 48 },
  { name: 'hdpi', size: 72 },
  { name: 'xhdpi', size: 96 },
  { name: 'xxhdpi', size: 144 },
  { name: 'xxxhdpi', size: 192 },
];

async function renderSvg(input, width, height) {
  return sharp(input, { density: 72 })
    .resize(width, height, { fit: 'fill' })
    .png({ compressionLevel: 9 })
    .toBuffer();
}

async function main() {
  for (const d of densities) {
    const size = Math.round(d.dp * d.scale);
    const bg = await renderSvg(bgSvg, size, size);
    const fg = await renderSvg(fgSvg, size, size);

    const dir = path.join(androidRes, `drawable-${d.name}`);
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, 'ic_launcher_background.png'), bg);
    fs.writeFileSync(path.join(dir, 'ic_launcher_foreground.png'), fg);
    console.log(`drawable-${d.name}: ${size}x${size}`);
  }

  for (const d of legacyDensities) {
    const size = d.size;
    const bg = await renderSvg(bgSvg, size, size);
    const fg = await renderSvg(fgSvg, size, size);
    const combined = await sharp(bg)
      .composite([{ input: fg, blend: 'over' }])
      .png({ compressionLevel: 9 })
      .toBuffer();

    const dir = path.join(androidRes, `mipmap-${d.name}`);
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, 'ic_launcher.png'), combined);
    console.log(`mipmap-${d.name}: ${size}x${size}`);
  }

  console.log('Android icons regenerated from /res SVGs.');
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
