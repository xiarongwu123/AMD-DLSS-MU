// Convert the existing MU brand mark to a multi-resolution Windows icon.
// Usage: swift scripts/build-app-icon.swift <logo.png> <output.ico>
import AppKit
import Foundation

let sourceURL = URL(fileURLWithPath: CommandLine.arguments[1])
let source = NSImage(contentsOf: sourceURL)!
var proposed = CGRect(origin: .zero, size: source.size)
let full = source.cgImage(forProposedRect: &proposed, context: nil, hints: nil)!
let mark = full.cropping(to: CGRect(x: 100, y: 120, width: 1120, height: 650))!
let sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
var payloads: [Data] = []
for size in sizes {
    let context = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8,
        bytesPerRow: size * 4, space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    context.interpolationQuality = .high
    let width = CGFloat(size) * 0.94
    let height = width * CGFloat(mark.height) / CGFloat(mark.width)
    context.draw(mark, in: CGRect(x: (CGFloat(size) - width) / 2,
        y: (CGFloat(size) - height) / 2, width: width, height: height))
    let bitmap = NSBitmapImageRep(cgImage: context.makeImage()!)
    payloads.append(bitmap.representation(using: .png, properties: [:])!)
}
var icon = Data()
func word(_ value: Int) { var v = UInt16(value).littleEndian; withUnsafeBytes(of: &v) { icon.append(contentsOf: $0) } }
func dword(_ value: Int) { var v = UInt32(value).littleEndian; withUnsafeBytes(of: &v) { icon.append(contentsOf: $0) } }
word(0); word(1); word(sizes.count)
var offset = 6 + 16 * sizes.count
for (index, size) in sizes.enumerated() {
    icon.append(contentsOf: [UInt8(size == 256 ? 0 : size), UInt8(size == 256 ? 0 : size), 0, 0])
    word(1); word(32); dword(payloads[index].count); dword(offset)
    offset += payloads[index].count
}
for payload in payloads { icon.append(payload) }
try icon.write(to: URL(fileURLWithPath: CommandLine.arguments[2]))
print("Created MU icon with \(sizes.count) sizes")
