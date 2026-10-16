import Cocoa
// Calls the Finder service exactly like the Services menu / Control+M does.
let pb = NSPasteboard(name: NSPasteboard.Name("mn-test-\(getpid())"))
pb.clearContents()
pb.writeObjects([URL(fileURLWithPath: CommandLine.arguments[1]) as NSURL])
let ok = NSPerformService("MN Player Önizleme", pb)
print("NSPerformService=\(ok)")
