Add-Type -AssemblyName PresentationCore, WindowsBase
$sourceImage = "d:\JournalApp\Group 1.png"

if (-not (Test-Path $sourceImage)) {
    Write-Error "Source image not found at $sourceImage"
    exit 1
}

# Read original dimensions of each PNG in Assets and resize the source image to match
Get-ChildItem "d:\JournalApp\Assets\*.png" | ForEach-Object {
    $targetFile = $_.FullName
    try {
        $pngStream = New-Object System.IO.FileStream -ArgumentList $targetFile, 'Open', 'Read', 'Read'
        $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::Create($pngStream, [System.Windows.Media.Imaging.BitmapCreateOptions]::None, [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $width = $decoder.Frames[0].PixelWidth
        $height = $decoder.Frames[0].PixelHeight
        $pngStream.Close()
        $pngStream.Dispose()
        
        Write-Host "Resizing to $($width)x$($height) for $($_.Name)..."
        
        # Load source
        $srcStream = New-Object System.IO.FileStream -ArgumentList $sourceImage, 'Open', 'Read', 'Read'
        $srcDecoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::Create($srcStream, [System.Windows.Media.Imaging.BitmapCreateOptions]::None, [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $srcFrame = $srcDecoder.Frames[0]
        
        # Resize using TransformedBitmap
        $scaleX = $width / $srcFrame.PixelWidth
        $scaleY = $height / $srcFrame.PixelHeight
        $transform = New-Object System.Windows.Media.ScaleTransform -ArgumentList $scaleX, $scaleY
        $resized = New-Object System.Windows.Media.Imaging.TransformedBitmap -ArgumentList $srcFrame, $transform
        $srcStream.Close()
        $srcStream.Dispose()
        
        # Save to target
        $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $null = $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($resized))
        $outStream = New-Object System.IO.FileStream -ArgumentList $targetFile, 'Create', 'Write', 'None'
        $encoder.Save($outStream)
        $outStream.Close()
        $outStream.Dispose()
        
        Write-Host "Successfully updated $($_.Name)"
    } catch {
        Write-Error "Failed to process $($_.Name): $_"
    }
}

# Also update AppIcon.ico if possible
# Let's generate a 256x256 png and convert it to a simple ICO file.
# A simple ICO file has a 6-byte header: 00 00 01 00 01 00 (Reserved, Type=1, ImageCount=1)
# and a 16-byte directory entry: Width, Height, Colors, Reserved, Planes, BPP, ImageSize, ImageOffset
# and then the raw PNG data. This is a standard feature of the ICO format (PNG inside ICO)!
$icoPath = "d:\JournalApp\Assets\AppIcon.ico"
try {
    Write-Host "Generating AppIcon.ico (PNG-in-ICO format)..."
    $width = 256
    $height = 256
    
    # Load source
    $srcStream = New-Object System.IO.FileStream -ArgumentList $sourceImage, 'Open', 'Read', 'Read'
    $srcDecoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::Create($srcStream, [System.Windows.Media.Imaging.BitmapCreateOptions]::None, [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $srcFrame = $srcDecoder.Frames[0]
    
    # Resize to 256x256
    $scaleX = $width / $srcFrame.PixelWidth
    $scaleY = $height / $srcFrame.PixelHeight
    $transform = New-Object System.Windows.Media.ScaleTransform -ArgumentList $scaleX, $scaleY
    $resized = New-Object System.Windows.Media.Imaging.TransformedBitmap -ArgumentList $srcFrame, $transform
    $srcStream.Close()
    $srcStream.Dispose()
    
    # Save to temporary memory stream
    $ms = New-Object System.IO.MemoryStream
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $null = $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($resized))
    $encoder.Save($ms)
    $pngBytes = $ms.ToArray()
    $ms.Close()
    $ms.Dispose()
    
    # Create ICO file
    $fs = New-Object System.IO.FileStream -ArgumentList $icoPath, 'Create', 'Write', 'None'
    
    # ICO Header (6 bytes)
    $fs.WriteByte(0) # Reserved
    $fs.WriteByte(0) # Reserved
    $fs.WriteByte(1) # Type (1 = icon)
    $fs.WriteByte(0)
    $fs.WriteByte(1) # Count (1 image)
    $fs.WriteByte(0)
    
    # Directory Entry (16 bytes)
    $fs.WriteByte(0) # Width (0 means 256)
    $fs.WriteByte(0) # Height (0 means 256)
    $fs.WriteByte(0) # Color count
    $fs.WriteByte(0) # Reserved
    $fs.WriteByte(1) # Color planes
    $fs.WriteByte(0)
    $fs.WriteByte(32) # Bits per pixel
    $fs.WriteByte(0)
    
    # Image size (4 bytes, little-endian)
    $size = $pngBytes.Length
    $fs.WriteByte($size -band 0xFF)
    $fs.WriteByte(($size -shr 8) -band 0xFF)
    $fs.WriteByte(($size -shr 16) -band 0xFF)
    $fs.WriteByte(($size -shr 24) -band 0xFF)
    
    # Image offset (4 bytes, little-endian, header + dir entry = 22)
    $offset = 22
    $fs.WriteByte($offset -band 0xFF)
    $fs.WriteByte(($offset -shr 8) -band 0xFF)
    $fs.WriteByte(($offset -shr 16) -band 0xFF)
    $fs.WriteByte(($offset -shr 24) -band 0xFF)
    
    # Write PNG data
    $fs.Write($pngBytes, 0, $pngBytes.Length)
    $fs.Close()
    $fs.Dispose()
    
    Write-Host "Successfully generated AppIcon.ico"
} catch {
    Write-Error "Failed to generate AppIcon.ico: $_"
}
