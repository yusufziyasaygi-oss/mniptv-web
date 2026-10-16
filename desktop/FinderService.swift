import Cocoa

// A windowless Services provider: opening a preview never launches the main player.
final class FinderService:NSObject,NSApplicationDelegate {
    var child:Process?
    func applicationDidFinishLaunching(_ notification:Notification){NSApp.servicesProvider=self}
    @objc func quickLook(_ pasteboard:NSPasteboard,userData:String?,error:AutoreleasingUnsafeMutablePointer<NSString?>) {
        var urls=pasteboard.readObjects(forClasses:[NSURL.self],options:[.urlReadingFileURLsOnly:true]) as? [URL] ?? []
        if urls.isEmpty,let paths=pasteboard.propertyList(forType:NSPasteboard.PasteboardType("NSFilenamesPboardType")) as? [String]{urls=paths.map{URL(fileURLWithPath:$0)}}
        guard let url=urls.first else{error.pointee="Bir medya dosyası seç.";return}
        for player in NSRunningApplication.runningApplications(withBundleIdentifier:"com.mavinokta.player"){player.hide()}
        if let child,child.isRunning{NSRunningApplication(processIdentifier:child.processIdentifier)?.terminate()}
        let process=Process()
        process.executableURL=Bundle.main.bundleURL.deletingLastPathComponent().appendingPathComponent("MN Preview.app/Contents/MacOS/MNPreview")
        process.arguments=["--quick-look",url.path]
        do{try process.run();child=process}catch let failure{error.pointee=failure.localizedDescription as NSString}
    }
}
let app=NSApplication.shared
let service=FinderService()
app.delegate=service
app.setActivationPolicy(.accessory)
app.run()
