#!/usr/bin/env swift
// Генерирует app-icon.png в стиле macOS: squircle, скругленные углы macOS HIG, тень, окантовка.
// Вход: src/Med.Ui/Assets/app-icon-mark.png (исходное изображение).
// Выход: src/Med.Ui/Assets/app-icon.png (1024×1024).

import AppKit
import Foundation

let size = 1024

func repoRoot() -> String {
    let script = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath()
    return script.deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().path
}

let root = repoRoot()
let markPath = "\(root)/src/Med.Ui/Assets/app-icon-mark.png"
let outPath = "\(root)/src/Med.Ui/Assets/app-icon.png"

guard let userImage = NSImage(contentsOfFile: markPath) else {
    fputs("Не найден или не читается \(markPath)\n", stderr)
    exit(1)
}

let squircleSide: CGFloat = 824
let cornerRadius = squircleSide * 0.2237
let xOffset: CGFloat = (CGFloat(size) - squircleSide) / 2
let yOffset: CGFloat = 110
let squircleRect = NSRect(x: xOffset, y: yOffset, width: squircleSide, height: squircleSide)

guard let rep = NSBitmapImageRep(
    bitmapDataPlanes: nil,
    pixelsWide: size,
    pixelsHigh: size,
    bitsPerSample: 8,
    samplesPerPixel: 4,
    hasAlpha: true,
    isPlanar: false,
    colorSpaceName: .deviceRGB,
    bytesPerRow: size * 4,
    bitsPerPixel: 32
) else {
    fputs("Не удалось создать NSBitmapImageRep\n", stderr)
    exit(1)
}

guard let context = NSGraphicsContext(bitmapImageRep: rep) else {
    fputs("Не удалось создать NSGraphicsContext\n", stderr)
    exit(1)
}

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = context

let ctx = context.cgContext
ctx.setAllowsAntialiasing(true)
ctx.setShouldAntialias(true)

let squirclePath = NSBezierPath(roundedRect: squircleRect, xRadius: cornerRadius, yRadius: cornerRadius)

// 1. Мягкая тень под squircle в стиле macOS HIG
ctx.saveGState()
let shadowColor = CGColor(gray: 0, alpha: 0.28)
ctx.setShadow(offset: CGSize(width: 0, height: -14), blur: 28, color: shadowColor)
NSColor.white.setFill()
squirclePath.fill()
ctx.restoreGState()

// 2. Вторичная легкая контактная тень для четкости контура в Dock
ctx.saveGState()
let contactShadowColor = CGColor(gray: 0, alpha: 0.12)
ctx.setShadow(offset: CGSize(width: 0, height: -6), blur: 10, color: contactShadowColor)
NSColor.white.setFill()
squirclePath.fill()
ctx.restoreGState()

// 3. Отрисовка контента внутри squircle
ctx.saveGState()
squirclePath.addClip()

NSColor.white.setFill()
squirclePath.fill()

userImage.draw(
    in: squircleRect,
    from: .zero,
    operation: .sourceOver,
    fraction: 1.0,
    respectFlipped: false,
    hints: [.interpolation: NSImageInterpolation.high]
)

// Тонкий внутренний ободок (hairline border) для идеального контраста на светлых обоях / доке
NSColor(white: 0.0, alpha: 0.10).setStroke()
squirclePath.lineWidth = 1.2
squirclePath.stroke()

ctx.restoreGState()

NSGraphicsContext.restoreGraphicsState()

guard let pngData = rep.representation(using: .png, properties: [:]) else {
    fputs("Не удалось сгенерировать PNG\n", stderr)
    exit(1)
}

do {
    try pngData.write(to: URL(fileURLWithPath: outPath))
    print("OK \(outPath)")
} catch {
    fputs("Ошибка записи \(outPath): \(error)\n", stderr)
    exit(1)
}
