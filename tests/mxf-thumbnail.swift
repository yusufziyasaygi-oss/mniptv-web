import AppKit
import QuickLookThumbnailing

// Ask the same system service Finder uses. Reject generic icons, blank images
// and a thumbnail taken from a later (green) frame.
let url = URL(fileURLWithPath: CommandLine.arguments[1])
let expected = CommandLine.arguments[2]
let request = QLThumbnailGenerator.Request(fileAt: url, size: CGSize(width: 160, height: 160),
                                           scale: 2, representationTypes: .thumbnail)
let generator = QLThumbnailGenerator.shared
generator.generateBestRepresentation(for: request) { result, error in
    guard let result = result, result.type == .thumbnail else {
        fputs("No thumbnail for \(url.lastPathComponent): \(String(describing: error))\n", stderr)
        exit(1)
    }
    let image = NSBitmapImageRep(cgImage: result.cgImage)
    guard let color = image.colorAt(x: image.pixelsWide / 2, y: image.pixelsHigh / 2)?
            .usingColorSpace(.sRGB) else { exit(1) }
    let matches = expected == "red"
        ? color.redComponent > 0.7 && color.greenComponent < 0.3 && color.blueComponent < 0.3
        : color.blueComponent > 0.7 && color.greenComponent < 0.3 && color.redComponent < 0.3
    print("\(url.lastPathComponent): \(image.pixelsWide)x\(image.pixelsHigh), center \(color)")
    if !matches {
        let file = url.deletingPathExtension().appendingPathExtension("png")
        try? image.representation(using: .png, properties: [:])?.write(to: file)
        exit(1)
    }
    exit(0)
}
DispatchQueue.main.asyncAfter(deadline: .now() + 30) {
    fputs("Timed out waiting for the Finder thumbnail\n", stderr)
    exit(1)
}
dispatchMain()
