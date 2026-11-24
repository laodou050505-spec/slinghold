#!/bin/bash
# 品牌图合成：用无头 Chrome 把 brand.html 截成透明底 PNG（本机没有 PIL），输出到 ArtSource/Brand/out/ 并拷进 Assets。
# 11-17 用户定稿 = 排法 ③：官方 TRIPO 标志单独放、原样不动；铭牌下面一行字“TRIPO XR 出品 / TRIPO XR DEVELOPERS”（英文是用户定的）；启动画面 = MADE WITH + TRIPO 标志（照 Unity 那张的横排）。
# 没有“TRIPO XR”这个标志，不要再把 XR 拼到标志后面（brand.html 里 v=1 / v=2 只是当时给用户比较用的）。
cd "$(dirname "$0")"; B="$(cd ../.. && pwd)"; O="$B/ArtSource/Brand/out"; CH="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
shot(){ # 名字 宽 高 查询串
  ( "$CH" --headless=new --no-proxy-server --user-data-dir=/tmp/figma_probe/prof2 --no-first-run --hide-scrollbars --force-device-scale-factor=1 --default-background-color=00000000 --allow-file-access-from-files --window-size=$2,$3 --screenshot="$O/$1.png" "file://$B/Tools/brand/brand.html?$4" >/dev/null 2>&1 ) & p=$!; ( sleep 40; kill $p 2>/dev/null ) >/dev/null 2>&1 & w=$!; wait $p 2>/dev/null; kill $w 2>/dev/null
  echo "$1 $(sips -g pixelWidth -g pixelHeight -g hasAlpha "$O/$1.png" 2>/dev/null | tail -3 | tr -s ' \n' ' ')"
}
shot plate_zh 1600 496 "m=plate&l=zh&v=3" 2>/dev/null
shot plate_en 1600 496 "m=plate&l=en&v=3" 2>/dev/null
shot made_with_tripo 763 250 "m=splash&v=3" 2>/dev/null
( "$CH" --headless=new --no-proxy-server --user-data-dir=/tmp/figma_probe/prof2 --no-first-run --hide-scrollbars --force-device-scale-factor=1 --allow-file-access-from-files --window-size=2048,1024 --screenshot="$O/made_with_tripo_vr.png" "file://$B/Tools/brand/brand.html?m=vrsplash&v=3" >/dev/null 2>&1 ) & p=$!; ( sleep 40; kill $p 2>/dev/null ) >/dev/null 2>&1 & w=$!; wait $p 2>/dev/null; kill $w 2>/dev/null   # 头显用的那张：不透明底
echo "made_with_tripo_vr $(sips -g pixelWidth -g pixelHeight "$O/made_with_tripo_vr.png" 2>/dev/null | tail -2 | tr -s ' \n' ' ')"
cp "$O/made_with_tripo_vr.png" "$B/Assets/_Monster/Art/Brand/"
cp "$O/plate_zh.png" "$O/plate_en.png" "$B/Assets/_Monster/Resources/Brand/"
cp "$O/made_with_tripo.png" "$B/Assets/_Monster/Art/Brand/"
