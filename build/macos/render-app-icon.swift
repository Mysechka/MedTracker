#!/usr/bin/env swift
// Генерирует app-icon.png в стиле macOS Big Sur: squircle, градиент Primary, белый glyph.
// Вход: src/Med.Ui/Assets/app-icon-mark.png (чёрный glyph на белом/прозрачном).
// Выход: src/Med.Ui/Assets/app-icon.png (1024×1024).

import AppKit
import Foundation

let size = 1024
let cornerRadius = CGFloat(size) * 0.2237
let logoScale = 0.58

func repoRoot() -> String {
    let script = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath()
    return script.deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().path
}

func loadMark(path: String) -> CGImage? {
    guard let image = NSImage(contentsOfFile: path),
          let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
        return nil
    }

    let width = cg.width
    let height = cg.height
    let colorSpace = CGColorSpaceCreateDeviceRGB()
    guard let ctx = CGContext(
        data: nil,
        width: width,
        height: height,
        bitsPerComponent: 8,
        bytesPerRow: width * 4,
        space: colorSpace,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    ) else { return nil }

    ctx.draw(cg, in: CGRect(x: 0, y: 0, width: width, height: height))
    guard let data = ctx.data else { return nil }
    let pixels = data.bindMemory(to: UInt8.self, capacity: width * height * 4)

    for y in 0..<height {
        for x in 0..<width {
            let i = (y * width + x) * 4
            let r = pixels[i]
            let g = pixels[i + 1]
            let b = pixels[i + 2]
            let luminance = (Int(r) + Int(g) + Int(b)) / 3

            if luminance > 235 {
                pixels[i] = 0
                pixels[i + 1] = 0
                pixels[i + 2] = 0
                pixels[i + 3] = 0
            } else {
                let alpha = UInt8(min(255, max(0, 255 - luminance)))
                pixels[i] = 255
                pixels[i + 1] = 255
                pixels[i + 2] = 255
                pixels[i + 3] = alpha
            }
        }
    }

    return ctx.makeImage()
}

func savePNG(_ image: NSImage, to path: String) throws {
    guard let tiff = image.tiffRepresentation,
          let rep = NSBitmapImageRep(data: tiff),
          let png = rep.representation(using: NSBitmapImageRep.FileType.png, properties: [:]) else {
        throw NSError(domain: "render-app-icon", code: 1, userInfo: [NSLocalizedDescriptionKey: "PNG encode failed"])
    }
    try png.write(to: URL(fileURLWithPath: path))
}

let root = repoRoot()
let markPath = "\(root)/src/Med.Ui/Assets/app-icon-mark.png"
let outPath = "\(root)/src/Med.Ui/Assets/app-icon.png"

guard let markCG = loadMark(path: markPath) else {
    fputs("Не найден или не читается \(markPath)\n", stderr)
    exit(1)
}

let canvas = NSImage(size: NSSize(width: size, height: size))
canvas.lockFocus()

let rect = NSRect(x: 0, y: 0, width: size, height: size)
let squircle = NSBezierPath(roundedRect: rect, xRadius: cornerRadius, yRadius: cornerRadius)
squircle.addClip()

let topGreen = NSColor(red: 0.36, green: 0.62, blue: 0.28, alpha: 1.0)
let bottomGreen = NSColor(red: 0.22, green: 0.42, blue: 0.125, alpha: 1.0)
if let gradient = NSGradient(colors: [topGreen, bottomGreen]) {
    gradient.draw(in: rect, angle: -90)
}

let highlight = NSGradient(
    colors: [
        NSColor(white: 1.0, alpha: 0.22),
        NSColor(white: 1.0, alpha: 0.0)
    ]
)
highlight?.draw(in: NSRect(x: 0, y: size / 2, width: size, height: size / 2), angle: -90)

let logoSide = CGFloat(size) * logoScale
let logoRect = NSRect(
    x: (CGFloat(size) - logoSide) / 2,
    y: (CGFloat(size) - logoSide) / 2,
    width: logoSide,
    height: logoSide
)

let markImage = NSImage(cgImage: markCG, size: NSSize(width: markCG.width, height: markCG.height))
markImage.draw(
    in: logoRect,
    from: .zero,
    operation: .sourceOver,
    fraction: 1.0,
    respectFlipped: false,
    hints: nil
)

canvas.unlockFocus()

do {
    try savePNG(canvas, to: outPath)
    print("OK \(outPath)")
} catch {
    fputs("Ошибка записи: \(error)\n", stderr)
    exit(1)
}
