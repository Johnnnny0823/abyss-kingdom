$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('ItemIconPixels' -as [type])) {
    Add-Type -ReferencedAssemblies @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.Rectangle].Assembly.Location) -TypeDefinition @'
public static class ItemIconPixels {
    public static int[] Bounds(System.Drawing.Bitmap image) {
        int left=image.Width, top=image.Height, right=-1, bottom=-1;
        for(int y=0;y<image.Height;y++) for(int x=0;x<image.Width;x++) {
            if(image.GetPixel(x,y).A<=8) continue;
            left=System.Math.Min(left,x); right=System.Math.Max(right,x);
            top=System.Math.Min(top,y); bottom=System.Math.Max(bottom,y);
        }
        return new int[]{left,top,right,bottom};
    }
    public static int[] Validate(System.Drawing.Bitmap image) {
        int opaque=0, transparent=0;
        for(int y=0;y<256;y++) for(int x=0;x<256;x++) {
            int alpha=image.GetPixel(x,y).A;
            if(alpha==0) transparent++; else opaque++;
            if((x<20 || x>=236 || y<20 || y>=236) && alpha>8)
                throw new System.Exception("Insufficient transparent margin");
        }
        return new int[]{opaque,transparent};
    }
}
'@
}
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskOutput = Join-Path $taskRoot 'art/source/items/samples/ready'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskReports = @()
foreach ($name in @('Speedup_General', 'Speedup_Building', 'Speedup_Research', 'Speedup_Magic', 'Speedup_Training',
    'HeroExpSmall', 'HeroExpMedium', 'HeroExpLarge', 'DomainScrollPower', 'DomainScrollTechnique',
    'DomainScrollSpeed', 'DomainScrollDurability', 'HeroShardAny', 'HeroShardHold', 'HeroShardVesper', 'HeroShardKarst')) {
    $source = [System.Drawing.Bitmap]::new((Join-Path $taskRoot "art/source/items/samples/$name.png"))
    $left, $top, $right, $bottom = [ItemIconPixels]::Bounds($source)
    if ($right -lt $left) { throw "Empty icon: $name" }
    $width = $right - $left + 1; $height = $bottom - $top + 1
    $scale = 212.0 / [Math]::Max($width, $height)
    $w = [int][Math]::Round($width * $scale); $h = [int][Math]::Round($height * $scale)
    $image = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($image)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $destination = [System.Drawing.Rectangle]::new([int]((256-$w)/2), [int]((256-$h)/2), $w, $h)
    $graphics.DrawImage($source, $destination, $left, $top, $width, $height, [System.Drawing.GraphicsUnit]::Pixel)
    $path = Join-Path $taskOutput "$name.png"
    $image.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $opaque, $transparent = [ItemIconPixels]::Validate($image)
    if ($opaque -eq 0 -or $transparent -eq 0) { throw "Invalid alpha: $name" }
    $taskReports += [ordered]@{name=$name;status='PASS';width=256;height=256;transparent_pixels=$transparent;
        source_bounds=@($left,$top,$right,$bottom);asset_path=$path;sha256=(Get-FileHash $path).Hash}
    $graphics.Dispose(); $image.Dispose(); $source.Dispose()
}
$taskReports | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $taskOutput 'validation.json') -Encoding utf8
$taskReports | ConvertTo-Json -Depth 5
