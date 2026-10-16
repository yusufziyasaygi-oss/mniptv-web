import Cocoa
import UniformTypeIdentifiers

let accent = NSColor(red: 59/255, green: 139/255, blue: 1, alpha: 1)
let videoExtensions = "mp4 m4v mov mkv avi webm wmv flv f4v mpg mpeg mpe m1v m2v mpv ts mts m2ts m2t vob ogv ogm 3gp 3g2 asf divx rm rmvb dv mxf nut nsv hevc h264 h265 y4m av1 amv mpegts".split(separator:" ").map(String.init)
let audioExtensions = "mp3 m4a aac wav flac ogg opus aiff aif wma ac3 dts ape caf au mka alac".split(separator:" ").map(String.init)
func makeEngine(headless: Bool = false) -> OpaquePointer? {
    let bundle=Bundle.main.bundleURL
    let base=Bundle.main.bundleIdentifier=="com.mavinokta.player.preview" ? bundle.deletingLastPathComponent().deletingLastPathComponent():bundle.appendingPathComponent("Contents")
    let root = base.appendingPathComponent("MacOS/plugins").path
    setenv("VLC_PLUGIN_PATH", root, 1)
    var args = ["--no-video-title-show", "--no-osd", "--no-stats", "--no-snapshot-preview", "--no-video-on-top", "--no-media-library", "--no-plugins-cache", "--quiet"]
    if headless { args += ["--vout=dummy", "--aout=dummy"] }
    let strings = args.map { strdup($0)! }
    defer { strings.forEach { free($0) } }
    let pointers = strings.map { UnsafePointer<CChar>($0) as UnsafePointer<CChar>? }
    return pointers.withUnsafeBufferPointer { libvlc_new(Int32(args.count), $0.baseAddress) }
}
func clockText(_ ms: Int64) -> String {
    let s = max(0, ms / 1000)
    return s >= 3600 ? String(format:"%lld:%02lld:%02lld",s/3600,(s/60)%60,s%60) : String(format:"%lld:%02lld",s/60,s%60)
}

// Display geometry includes anamorphic pixels and phone rotation metadata.
func displayAspect(_ mp: OpaquePointer) -> CGFloat? {
    var w:UInt32=0,h:UInt32=0
    guard libvlc_video_get_size(mp,0,&w,&h)==0,w>0,h>0 else{return nil}
    var ratio=CGFloat(w)/CGFloat(h)
    if let media=libvlc_media_player_get_media(mp) {
        defer{libvlc_media_release(media)}
        var tracks:UnsafeMutablePointer<UnsafeMutablePointer<libvlc_media_track_t>?>?
        let count=libvlc_media_tracks_get(media,&tracks)
        defer{if let tracks{libvlc_media_tracks_release(tracks,count)}}
        if let tracks {
            let selected=libvlc_video_get_track(mp)
            for i in 0..<Int(count) {
                guard let t=tracks[i],t.pointee.i_type==libvlc_track_video,t.pointee.i_id==selected,let v=t.pointee.video else{continue}
                let video=v.pointee
                if video.i_width>0 && video.i_height>0{ratio=CGFloat(video.i_width)/CGFloat(video.i_height)}
                if video.i_sar_num>0 && video.i_sar_den>0{ratio *= CGFloat(video.i_sar_num)/CGFloat(video.i_sar_den)}
                if video.i_orientation.rawValue>=libvlc_video_orient_left_top.rawValue{ratio=1/ratio}
                break
            }
        }
    }
    return ratio.isFinite && ratio>0 ? ratio:nil
}

// Command-line codec verification uses the same bundled engine, with no GUI or audio output.
if CommandLine.arguments.contains("--verify-media") {
    guard let engine = makeEngine(headless:true), let mp = libvlc_media_player_new(engine) else { print("ENGINE_ERROR"); exit(2) }
    var failed = false
    for path in CommandLine.arguments.dropFirst(2) {
        let media = path.withCString { libvlc_media_new_path(engine,$0) }!
        libvlc_media_player_set_media(mp,media); libvlc_media_release(media)
        let result = libvlc_media_player_play(mp)
        let deadline = Date().addingTimeInterval(12)
        var success = false
        while Date() < deadline {
            let state = libvlc_media_player_get_state(mp)
            if state == libvlc_Error { break }
            if libvlc_media_player_get_time(mp) > 250 || state == libvlc_Ended { success = true; break }
            Thread.sleep(forTimeInterval:0.1)
        }
        var w:UInt32=0,h:UInt32=0
        _ = libvlc_video_get_size(mp,0,&w,&h)
        print("\(success && result == 0 ? "PASS" : "FAIL") \(URL(fileURLWithPath:path).lastPathComponent) \(w)x\(h) \(libvlc_media_player_get_time(mp))ms")
        if !success || result != 0 { failed = true }
        libvlc_media_player_stop(mp)
    }
    libvlc_media_player_release(mp);libvlc_release(engine);exit(failed ? 1 : 0)
}

final class PlayerWindow:NSWindow {
    var preview=false
    override func cancelOperation(_ sender:Any?){if preview{performClose(sender)}else{super.cancelOperation(sender)}}
}

final class Surface: NSView {
    weak var owner: AppDelegate?
    override var acceptsFirstResponder: Bool { true }
    override func draw(_ dirtyRect: NSRect) { NSColor.black.setFill();bounds.fill() }
    override func mouseDown(with event:NSEvent) { if event.clickCount==2 { owner?.fullScreen() } else { owner?.togglePlay() } }
    override func keyDown(with event:NSEvent) { owner?.key(event) }
    override func draggingEntered(_ sender:NSDraggingInfo) -> NSDragOperation { .copy }
    override func performDragOperation(_ sender:NSDraggingInfo) -> Bool {
        let urls=sender.draggingPasteboard.readObjects(forClasses:[NSURL.self],options:[.urlReadingFileURLsOnly:true]) as? [URL] ?? []
        owner?.add(urls);return !urls.isEmpty
    }
    override func mouseMoved(with event:NSEvent) { owner?.wake() }
    override func updateTrackingAreas() {
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect:bounds,options:[.mouseMoved,.activeInKeyWindow,.inVisibleRect],owner:self,userInfo:nil))
        super.updateTrackingAreas()
    }
}
final class Root: NSView {
    weak var owner:AppDelegate?
    override func layout() { super.layout();owner?.layout() }
    override var acceptsFirstResponder:Bool { true }
    override func keyDown(with event:NSEvent) { owner?.key(event) }
}
final class Timeline: NSSlider {
    var dragging = false
    override func mouseDown(with event:NSEvent) { dragging=true;super.mouseDown(with:event);dragging=false }
}
final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, NSTableViewDataSource, NSTableViewDelegate {
    var window:NSWindow!
    var root:Root!
    var surface:Surface!
    var controls:NSVisualEffectView!
    var tools:NSVisualEffectView!
    var engine:OpaquePointer!
    var mp:OpaquePointer!
    var timer:Timer?
    var hideTimer:Timer?
    var sleepTimer:Timer?
    var queue:[URL]=[]
    var index = -1
    var pending:[URL]=[]
    var repeatOne=false
    var autoplay=true
    var savedVolume:Int32=100
    var lastState=libvlc_NothingSpecial
    var errorShown=false
    var playButton:NSButton!
    var nextButton:NSButton!
    var muteButton:NSButton!
    var center:NSButton!
    var timeLabel:NSTextField!
    var status:NSTextField!
    var progress:Timeline!
    var volume:NSSlider!
    var rightButtons:[NSButton]=[]
    var table:NSTableView!
    var queuePanel:NSPanel?
    var positionSaved=Date.distantPast
    var pendingSeek:Int64?
    var playbackRate:Float=1
    var initializationFailed=false
    let engineQueue=DispatchQueue(label:"com.mavinokta.player.engine")
    var tickPending=false
    var lastTime:Int64=0
    var lastLength:Int64=0
    var seekable=false
    var terminating=false
    var generation=0
    var videoAspect:CGFloat?
    var regularFrame:NSRect?
    let previewMode=CommandLine.arguments.contains("--quick-look") || Bundle.main.bundleIdentifier=="com.mavinokta.player.preview"
    var previousApp:NSRunningApplication?
    var activeMedia:URL? { queue.indices.contains(index) ? queue[index] : nil }

    func applicationDidFinishLaunching(_ notification:Notification) {
        NSAppleEventManager.shared().setEventHandler(self, andSelector: #selector(receiveStream(_:reply:)), forEventClass: AEEventClass(0x4755524c), andEventID: AEEventID(0x4755524c))
        previousApp=NSWorkspace.shared.frontmostApplication
        if previewMode{autoplay=false}
        NSApp.appearance=NSAppearance(named:.darkAqua)
        guard let instance=makeEngine(),let player=libvlc_media_player_new(instance) else {
            initializationFailed=true;let alert=NSAlert();alert.messageText="Medya motoru başlatılamadı";alert.informativeText="Uygulamayı yeniden kurmayı dene.";alert.runModal();NSApp.terminate(nil);return
        }
        engine=instance;mp=player
        root=Root();root.owner=self;root.wantsLayer=true;root.layer?.backgroundColor=NSColor.black.cgColor
        surface=Surface();surface.owner=self;surface.registerForDraggedTypes([.fileURL]);root.addSubview(surface)
        window=PlayerWindow(contentRect:NSRect(x:0,y:0,width:1120,height:680),styleMask:[.titled,.closable,.miniaturizable,.resizable],backing:.buffered,defer:false)
        (window as? PlayerWindow)?.preview=previewMode
        if previewMode{window.level = .floating}
        window.title="MN Player";window.contentMinSize=NSSize(width:300,height:200);window.contentView=root;window.delegate=self;window.acceptsMouseMovedEvents=true
        window.backgroundColor = .black;window.setFrameAutosaveName("MNPlayerWindow")
        window.center();window.makeKeyAndOrderFront(nil)
        libvlc_media_player_set_nsobject(mp,Unmanaged.passUnretained(surface).toOpaque())
        libvlc_video_set_mouse_input(mp,0);libvlc_video_set_key_input(mp,0)
        let prefs=UserDefaults.standard
        savedVolume=prefs.object(forKey:"volume") == nil ? 80 : Int32(prefs.integer(forKey:"volume"))
        playbackRate=prefs.object(forKey:"rate") == nil ? 1 : prefs.float(forKey:"rate")
        libvlc_audio_set_volume(mp,savedVolume)
        createControls();buildMenu();layout()
        if !previewMode,Bundle.main.bundleURL.path=="/Applications/MN Player.app",!prefs.bool(forKey:"initialSetupCompleted") {
            Task { _ = await configureInstallation() }
        }
        timer=Timer.scheduledTimer(withTimeInterval:0.2,repeats:true) { [weak self] _ in self?.tick() }
        window.makeFirstResponder(surface);NSApp.activate(ignoringOtherApps:true)
        if !pending.isEmpty { let items=pending;pending=[];add(items) }
        let args=CommandLine.arguments.dropFirst().filter { !$0.hasPrefix("-") }
        if !args.isEmpty {
            let urls=args.map { URL(fileURLWithPath:$0) }
            if previewMode,let selected=urls.first {
                let siblings=(try? FileManager.default.contentsOfDirectory(at:selected.deletingLastPathComponent(),includingPropertiesForKeys:[.isRegularFileKey],options:[.skipsHiddenFiles])) ?? []
                queue=siblings.filter{videoExtensions.contains($0.pathExtension.lowercased()) && (try? $0.resourceValues(forKeys:[.isRegularFileKey]).isRegularFile)==true}.sorted{$0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending}
                if let position=queue.firstIndex(where: { $0.path.precomposedStringWithCanonicalMapping == selected.path.precomposedStringWithCanonicalMapping }){play(position)}else{add([selected])}
            }else{add(urls)}
        }
    }
    func makeButton(_ symbol:String,_ label:String,_ action:Selector) -> NSButton {
        let button=NSButton(image:NSImage(systemSymbolName:symbol,accessibilityDescription:label) ?? NSImage(),target:self,action:action)
        button.isBordered=false;button.contentTintColor = .white;button.toolTip=label;button.setAccessibilityLabel(label);button.imageScaling = .scaleProportionallyDown
        return button
    }
    func hud() -> NSVisualEffectView {
        let v=NSVisualEffectView();v.material = .hudWindow;v.blendingMode = .withinWindow;v.state = .active;v.wantsLayer=true;return v
    }
    func createControls() {
        controls=hud();root.addSubview(controls)
        progress=Timeline(value:0,minValue:0,maxValue:1,target:self,action:#selector(seekBar(_:)));progress.isContinuous=true;progress.controlSize = .small;progress.setAccessibilityLabel("Oynatma konumu");controls.addSubview(progress)
        playButton=makeButton("play.fill","Oynat / duraklat",#selector(togglePlay));controls.addSubview(playButton)
        nextButton=makeButton("forward.end.fill","Sonraki video",#selector(next));controls.addSubview(nextButton)
        muteButton=makeButton("speaker.wave.2.fill","Sessiz",#selector(mute));controls.addSubview(muteButton)
        volume=NSSlider(value:Double(savedVolume),minValue:0,maxValue:100,target:self,action:#selector(volumeChanged(_:)));volume.isContinuous=true;volume.controlSize = .small;volume.setAccessibilityLabel("Ses düzeyi");controls.addSubview(volume)
        timeLabel=NSTextField(labelWithString:"0:00 / 0:00");timeLabel.font=NSFont.monospacedDigitSystemFont(ofSize:12,weight:.regular);timeLabel.textColor = .white;controls.addSubview(timeLabel)
        rightButtons=[makeButton("captions.bubble","Altyazılar",#selector(captionsMenu(_:))),makeButton("gearshape.fill","Ayarlar",#selector(settings(_:))),makeButton("pip","Mini oynatıcı",#selector(mini)),makeButton("arrow.up.left.and.arrow.down.right","Tam ekran",#selector(fullScreen))]
        rightButtons.forEach { controls.addSubview($0) }
        tools=hud();tools.layer?.cornerRadius=8;root.addSubview(tools)
        let buttons=[makeButton("folder","Medya aç",#selector(openMedia)),makeButton("link","Bağlantı aç",#selector(openURL)),makeButton("text.badge.plus","Altyazı ekle",#selector(openCaptions)),makeButton("list.bullet","Oynatma listesi",#selector(showQueue)),makeButton("keyboard","Klavye kısayolları",#selector(shortcuts))]
        for (i,b) in buttons.enumerated(){b.frame=NSRect(x:CGFloat(i)*38+4,y:4,width:34,height:32);tools.addSubview(b)}
        center=makeButton("play.fill","Medya aç / oynat",#selector(togglePlay));center.contentTintColor=accent;root.addSubview(center)
        status=NSTextField(labelWithString:"");status.font = .systemFont(ofSize:13);status.textColor = .white;status.alignment = .center;status.isHidden=true;root.addSubview(status)
    }
    func layout() {
        guard controls != nil else { return }
        let b=root.bounds
        surface.frame=b
        let compact=b.width<600
        controls.frame=NSRect(x:0,y:0,width:b.width,height:compact ? 110:74)
        progress.frame=NSRect(x:12,y:compact ? 86:50,width:b.width-24,height:20)
        let row:CGFloat=compact ? 43:7
        playButton.frame=NSRect(x:12,y:row,width:36,height:36)
        nextButton.frame=NSRect(x:52,y:row,width:32,height:36)
        muteButton.frame=NSRect(x:89,y:row,width:32,height:36)
        volume.frame=NSRect(x:126,y:row+11,width:72,height:16)
        volume.isHidden=compact
        timeLabel.frame=NSRect(x:compact ? 128:210,y:row+10,width:compact ? b.width-140:175,height:18)
        timeLabel.font=NSFont.monospacedDigitSystemFont(ofSize:compact ? 11:12,weight:.regular)
        for (i,button) in rightButtons.enumerated(){button.frame=NSRect(x:b.width-CGFloat(4-i)*42-10,y:7,width:36,height:36)}
        tools.frame=NSRect(x:b.width-208,y:b.height-52,width:198,height:40)
        center.frame=NSRect(x:b.midX-35,y:b.midY-35,width:70,height:70)
        status.frame=NSRect(x:16,y:b.midY+65,width:b.width-32,height:45)
    }
    func add(_ urls:[URL]) {
        let subs=urls.filter { ["srt","vtt","ass","ssa","sub"].contains($0.pathExtension.lowercased()) }
        let media=urls.filter { !subs.contains($0) && (!$0.isFileURL || ((try? $0.resourceValues(forKeys:[.isDirectoryKey]).isDirectory) != true)) }
        if !media.isEmpty { let start=queue.count;queue += media;play(start) }
        subs.forEach(addSubtitle)
        table?.reloadData()
    }
    func play(_ i:Int) {
        guard queue.indices.contains(i) else { return }
        savePosition();generation += 1;videoAspect=nil;index=i;errorShown=false;lastState=libvlc_NothingSpecial
        // Prevent previous media timing from being stored for the new media URL.
        lastTime=0;lastLength=0;seekable=false;positionSaved=Date()
        let url=queue[i]
        let positions=UserDefaults.standard.dictionary(forKey:"positions") as? [String:Int64] ?? [:]
        pendingSeek=previewMode ? nil:positions[url.absoluteString]
        engineQueue.async { [self] in
            libvlc_media_player_stop(mp)
            let media=url.isFileURL ? url.path.withCString { libvlc_media_new_path(engine,$0) } : url.absoluteString.withCString { libvlc_media_new_location(engine,$0) }
            guard let media else { DispatchQueue.main.async {self.notify("Dosya açılamadı")};return }
            libvlc_media_player_set_media(mp,media);libvlc_media_release(media)
            _ = libvlc_media_player_play(mp);libvlc_audio_set_volume(mp,savedVolume)
        }
        window.representedURL=url.isFileURL ? url : nil
        window.title=previewMode ? url.lastPathComponent : "MN Player";table?.reloadData();window.makeFirstResponder(surface);wake()
    }
    func tick() {
        guard mp != nil,!tickPending else {return}
        tickPending=true
        let revision=generation
        engineQueue.async { [self] in
            let state=libvlc_media_player_get_state(mp),time=libvlc_media_player_get_time(mp),length=libvlc_media_player_get_length(mp),canSeek=libvlc_media_player_is_seekable(mp)==1
            let aspect=displayAspect(mp)
            DispatchQueue.main.async { [self] in
                tickPending=false;guard !terminating,revision==generation else{return};lastTime=time;lastLength=length;seekable=canSeek
                if let aspect,videoAspect==nil || abs(aspect-(videoAspect ?? aspect))>0.005{videoAspect=aspect;fitVideoWindow()}
                if let seek=pendingSeek,state==libvlc_Playing,length>0 {
                    pendingSeek=nil
                    let resume=seek>5000 && seek<length-5000
                    engineQueue.async { [self] in if resume{libvlc_media_player_set_time(mp,seek)};_ = libvlc_media_player_set_rate(mp,playbackRate) }
                } else if state==libvlc_Playing && lastState != libvlc_Playing {engineQueue.async {[self] in _ = libvlc_media_player_set_rate(mp,playbackRate)}}
                if !progress.dragging{progress.doubleValue=length>0 ? Double(max(0,time))/Double(length):0}
                progress.isEnabled=canSeek;timeLabel.stringValue="\(clockText(time)) / \(length>0 ? clockText(length):"0:00")"
                playButton.image=NSImage(systemSymbolName:state==libvlc_Playing ? "pause.fill":"play.fill",accessibilityDescription:nil)
                center.isHidden=activeMedia != nil;nextButton.isEnabled=index+1<queue.count
                if state==libvlc_Error && !errorShown{errorShown=true;wake();showError()}
                if state==libvlc_Ended && lastState != libvlc_Ended{clearPosition();lastState=state;if repeatOne{play(index);return};if autoplay && index+1<queue.count{play(index+1);return};wake()}
                if Date().timeIntervalSince(positionSaved)>5{savePosition();positionSaved=Date()};lastState=state
            }
        }
    }
    @objc func togglePlay() {
        guard activeMedia != nil else {openMedia();return}
        if lastState==libvlc_Ended || lastState==libvlc_Error {play(index)}
        else {engineQueue.async {[self] in if libvlc_media_player_is_playing(mp)==1{libvlc_media_player_set_pause(mp,1)}else{libvlc_media_player_set_pause(mp,0);_ = libvlc_media_player_play(mp)}}}
        window.makeFirstResponder(surface);wake()
    }
    @objc func next(){if index+1<queue.count { play(index+1) }}
    @objc func previous(){if index>0 { play(index-1) }else{engineQueue.async {[self] in libvlc_media_player_set_time(mp,0)}}}
    @objc func seekBar(_ sender:NSSlider){let length=lastLength;let position=Int64(sender.doubleValue*Double(length));if length>0{engineQueue.async {[self] in libvlc_media_player_set_time(mp,position)}};wake()}
    func seek(_ delta:Int64){guard seekable else{return};let position=max(0,min(lastLength,lastTime+delta));engineQueue.async {[self] in libvlc_media_player_set_time(mp,position)};wake()}
    @objc func volumeChanged(_ sender:NSSlider){savedVolume=Int32(sender.intValue);engineQueue.async {[self] in libvlc_audio_set_volume(mp,savedVolume);libvlc_audio_set_mute(mp,0)};UserDefaults.standard.set(savedVolume,forKey:"volume");muteButton.image=NSImage(systemSymbolName:savedVolume==0 ? "speaker.slash.fill":"speaker.wave.2.fill",accessibilityDescription:nil)}
    @objc func mute(){engineQueue.async {[self] in let value=libvlc_audio_get_mute(mp)==1 ? 0:1;libvlc_audio_set_mute(mp,Int32(value));DispatchQueue.main.async {self.muteButton.image=NSImage(systemSymbolName:value==1 ? "speaker.slash.fill":"speaker.wave.2.fill",accessibilityDescription:nil)}};wake()}
    @objc func fullScreen(){window.toggleFullScreen(nil);wake()}
    func fitVideoWindow() {
        guard let ratio=videoAspect,!window.styleMask.contains(.fullScreen),let screen=window.screen ?? NSScreen.main else{return}
        let available=screen.visibleFrame.insetBy(dx:16,dy:16)
        let chrome=window.frame.height-root.bounds.height
        let mini=window.level == .floating && !previewMode
        let maxWidth=min(available.width,mini ? 420:1120)
        let maxHeight=min(available.height-chrome,mini ? 440:800)
        let height=min(maxHeight,maxWidth/ratio)
        let size=NSSize(width:height*ratio,height:height)
        // Resize handles keep the image proportions, so no side bars appear.
        window.contentMinSize=NSSize(width:min(300,size.width),height:min(200,size.height))
        window.contentAspectRatio=NSSize(width:ratio,height:1)
        var frame=window.frameRect(forContentRect:NSRect(origin:.zero,size:size))
        frame.origin=NSPoint(x:window.frame.midX-frame.width/2,y:window.frame.midY-frame.height/2)
        frame.origin.x=max(available.minX,min(available.maxX-frame.width,frame.minX))
        frame.origin.y=max(available.minY,min(available.maxY-frame.height,frame.minY))
        window.setFrame(frame,display:true)
        layout()
    }
    func windowDidExitFullScreen(_ notification:Notification){fitVideoWindow();wake()}
    @objc func mini(){
        if window.styleMask.contains(.fullScreen){return}
        if window.level == .floating {
            window.level = .normal
            if let frame=regularFrame{window.setFrame(frame,display:true)}else{fitVideoWindow()}
            regularFrame=nil
        }else{
            regularFrame=window.frame;window.level = .floating
            if videoAspect != nil{fitVideoWindow()}else{window.setContentSize(NSSize(width:420,height:260))}
        }
        layout();wake()
    }
    @objc func openMedia(){let panel=NSOpenPanel();panel.allowsMultipleSelection=true;panel.canChooseDirectories=false;panel.beginSheetModal(for:window){[weak self] result in if result == .OK{self?.add(panel.urls)}}}
    @objc func openURL(){let alert=NSAlert();alert.messageText="Medya bağlantısı";let input=NSTextField(frame:NSRect(x:0,y:0,width:420,height:26));input.placeholderString="https://…";alert.accessoryView=input;alert.addButton(withTitle:"Aç");alert.addButton(withTitle:"İptal");alert.beginSheetModal(for:window){[weak self] result in if result == .alertFirstButtonReturn,let url=URL(string:input.stringValue),["https","http","rtsp","rtmp","udp","rtp"].contains(url.scheme?.lowercased() ?? ""){self?.add([url])}}}
    @objc func openCaptions(){let panel=NSOpenPanel();panel.allowsMultipleSelection=true;panel.allowedContentTypes=["srt","vtt","ass","ssa","sub"].compactMap{UTType(filenameExtension:$0)};panel.beginSheetModal(for:window){[weak self] result in if result == .OK{panel.urls.forEach{self?.addSubtitle($0)}}}}
    func addSubtitle(_ url:URL){guard activeMedia != nil else {notify("Önce medya aç");return};engineQueue.async {[self] in let result=url.absoluteString.withCString{libvlc_media_player_add_slave(mp,libvlc_media_slave_type_subtitle,$0,true)};DispatchQueue.main.async {self.notify(result==0 ? "Altyazı eklendi":"Altyazı açılamadı")}}}
    func trackMenu(audio:Bool) -> NSMenu {
        let menu=NSMenu();let first=audio ? libvlc_audio_get_track_description(mp):libvlc_video_get_spu_description(mp)
        var cursor=first
        while let track=cursor {
            let t=track.pointee,label=t.psz_name.map{String(cString:$0)} ?? "Kanal \(t.i_id)"
            let item=NSMenuItem(title:label,action:audio ? #selector(audioTrack(_:)):#selector(subtitleTrack(_:)),keyEquivalent:"");item.target=self;item.tag=Int(t.i_id)
            item.state=(audio ? libvlc_audio_get_track(mp):libvlc_video_get_spu(mp))==t.i_id ? .on:.off
            menu.addItem(item);cursor=t.p_next
        }
        if first != nil {libvlc_track_description_list_release(first)}
        if !audio {menu.addItem(.separator());let add=NSMenuItem(title:"Altyazı dosyası ekle…",action:#selector(openCaptions),keyEquivalent:"");add.target=self;menu.addItem(add)}
        return menu
    }
    @objc func audioTrack(_ item:NSMenuItem){let id=Int32(item.tag);engineQueue.async {[self] in _ = libvlc_audio_set_track(mp,id)}}
    @objc func subtitleTrack(_ item:NSMenuItem){let id=Int32(item.tag);engineQueue.async {[self] in _ = libvlc_video_set_spu(mp,id)}}
    @objc func captionsMenu(_ sender:NSButton){trackMenu(audio:false).popUp(positioning:nil,at:NSPoint(x:0,y:sender.bounds.height+8),in:sender);wake()}
    func item(_ title:String,_ action:Selector,_ key:String="",tag:Int=0) -> NSMenuItem {let item=NSMenuItem(title:title,action:action,keyEquivalent:key);item.target=self;item.tag=tag;return item}
    @objc func settings(_ sender:NSButton){let menu=NSMenu();let speed=NSMenuItem(title:"Oynatma hızı",action:nil,keyEquivalent:"");let submenu=NSMenu();for rate in [0.25,0.5,0.75,1,1.25,1.5,1.75,2.0]{let i=item(rate==1 ? "Normal":"\(rate)×",#selector(rate(_:)),tag:Int(rate*100));i.state=abs(Double(playbackRate)-rate)<0.01 ? .on:.off;submenu.addItem(i)};speed.submenu=submenu;menu.addItem(speed)
        let audio=NSMenuItem(title:"Ses kanalı",action:nil,keyEquivalent:"");audio.submenu=trackMenu(audio:true);menu.addItem(audio)
        let loop=item("Döngü",#selector(loop));loop.state=repeatOne ? .on:.off;menu.addItem(loop)
        let auto=item("Otomatik sonraki video",#selector(toggleAutoplay));auto.state=autoplay ? .on:.off;menu.addItem(auto)
        let sleep=NSMenuItem(title:"Uyku zamanlayıcısı",action:nil,keyEquivalent:"");let sleeps=NSMenu();for minutes in [0,15,30,60,90]{sleeps.addItem(item(minutes==0 ? "Kapalı":"\(minutes) dakika",#selector(sleep(_:)),tag:minutes))};sleep.submenu=sleeps;menu.addItem(sleep)
        menu.addItem(item("Medya bilgisi",#selector(info)));menu.popUp(positioning:nil,at:NSPoint(x:0,y:sender.bounds.height+8),in:sender);wake()
    }
    @objc func rate(_ item:NSMenuItem){let requested=Float(item.tag)/100;if libvlc_media_player_set_rate(mp,requested)==0{playbackRate=requested;UserDefaults.standard.set(requested,forKey:"rate")}else{notify("Bu kaynakta hız değiştirilemiyor")}}
    @objc func loop(){repeatOne.toggle()}
    @objc func toggleAutoplay(){autoplay.toggle()}
    @objc func sleep(_ item:NSMenuItem){sleepTimer?.invalidate();if item.tag>0{sleepTimer=Timer.scheduledTimer(withTimeInterval:Double(item.tag)*60,repeats:false){[weak self] _ in guard let self else{return};self.engineQueue.async {libvlc_media_player_set_pause(self.mp,1)};self.wake()}}}
    @objc func info(){guard let url=activeMedia else{return};var w:UInt32=0,h:UInt32=0;_ = libvlc_video_get_size(mp,0,&w,&h);let alert=NSAlert();alert.messageText=url.lastPathComponent;alert.informativeText="\(w) × \(h)\n\(clockText(libvlc_media_player_get_length(mp)))\nVLC \(String(cString:libvlc_get_version()))";alert.beginSheetModal(for:window)}
    @objc func shortcuts(){let alert=NSAlert();alert.messageText="Klavye kısayolları";alert.informativeText="Space / K: Oynat / duraklat\nJ / L: 10 saniye • ← / →: 5 saniye\n↑ / ↓: Ses • M: Sessiz\nF: Tam ekran • I: Mini oynatıcı\nC: Altyazı aç / kapat\n0–9: Sürenin %0–90 noktasına git\n.: Duraklatılmış videoda sonraki kare\n⌘O: Dosya • ⌘L: Bağlantı • ⌘S: Altyazı";alert.beginSheetModal(for:window)}
    func key(_ event:NSEvent){if previewMode {
            if event.keyCode==53{window.performClose(nil);return}
            if event.keyCode==126{previous();wake();return}
            if event.keyCode==125{next();wake();return}
        };if event.modifierFlags.contains(.command){return};switch event.charactersIgnoringModifiers?.lowercased() ?? ""{case " ","k":togglePlay();case "j":seek(-10000);case "l":seek(10000);case "m":mute();case "f":fullScreen();case "i":mini();case "c":engineQueue.async {[self] in let n=libvlc_video_get_spu(mp);let head=libvlc_video_get_spu_description(mp);var p=head;var id:Int32 = -1;while let t=p{if t.pointee.i_id>=0{id=t.pointee.i_id;break};p=t.pointee.p_next};_ = libvlc_video_set_spu(mp,n>=0 ? -1:id);if head != nil{libvlc_track_description_list_release(head)}};case ".":engineQueue.async {[self] in libvlc_media_player_next_frame(mp)};case "?":shortcuts();default:if event.keyCode==53 && window.styleMask.contains(.fullScreen){fullScreen()}else if event.keyCode==123{seek(-5000)}else if event.keyCode==124{seek(5000)}else if event.keyCode==126 || event.keyCode==125{volume.doubleValue=max(0,min(100,volume.doubleValue+(event.keyCode==126 ? 5:-5)));volumeChanged(volume)}else if let s=event.charactersIgnoringModifiers,let n=Int(s),n>=0,n<=9{let pos=lastLength*Int64(n)/10;engineQueue.async {[self] in libvlc_media_player_set_time(mp,pos)}}};wake()}
    func wake(){controls?.isHidden=false;tools?.isHidden=previewMode;hideTimer?.invalidate();hideTimer=Timer.scheduledTimer(withTimeInterval:3,repeats:false){[weak self] _ in guard let self,self.lastState==libvlc_Playing,self.window.attachedSheet==nil else{return};self.controls.isHidden=true;self.tools.isHidden=true}}
    func notify(_ text:String){status.stringValue=text;status.isHidden=false;DispatchQueue.main.asyncAfter(deadline:.now()+2.5){[weak self] in self?.status.isHidden=true}}
    func showError(){let alert=NSAlert();alert.messageText="Medya açılamadı";alert.informativeText="Dosya bozuk, şifreli, erişilemez veya bu motorun desteklemediği bir biçimde olabilir.";alert.addButton(withTitle:"Tamam");alert.addButton(withTitle:"Başka dosya aç");alert.beginSheetModal(for:window){[weak self] result in if result == .alertSecondButtonReturn{self?.openMedia()}}}
    func savePosition(){guard !previewMode else{return};guard let url=activeMedia,mp != nil else{return};let t=lastTime,l=lastLength;guard l>0,t>5000 else{return};var positions=UserDefaults.standard.dictionary(forKey:"positions") as? [String:Int64] ?? [:];if t<l-5000{positions[url.absoluteString]=t}else{positions.removeValue(forKey:url.absoluteString)};if positions.count>200{positions=Dictionary(uniqueKeysWithValues:positions.prefix(200).map{($0.key,$0.value)})};UserDefaults.standard.set(positions,forKey:"positions")}
    func clearPosition(){guard !previewMode else{return};guard let url=activeMedia else{return};var positions=UserDefaults.standard.dictionary(forKey:"positions") as? [String:Int64] ?? [:];positions.removeValue(forKey:url.absoluteString);UserDefaults.standard.set(positions,forKey:"positions")}
    @objc func showQueue(){if let panel=queuePanel{panel.makeKeyAndOrderFront(nil);return};let panel=NSPanel(contentRect:NSRect(x:0,y:0,width:440,height:400),styleMask:[.titled,.closable,.resizable],backing:.buffered,defer:false);panel.title="Oynatma listesi";panel.isReleasedWhenClosed=false
        let scroll=NSScrollView(frame:NSRect(x:0,y:0,width:440,height:400));scroll.autoresizingMask=[.width,.height];scroll.hasVerticalScroller=true
        table=NSTableView();let col=NSTableColumn(identifier:NSUserInterfaceItemIdentifier("file"));col.title="Dosya";col.width=420;table.addTableColumn(col);table.headerView=nil;table.rowHeight=30;table.dataSource=self;table.delegate=self;table.doubleAction=#selector(queueSelect);table.target=self;scroll.documentView=table;panel.contentView=scroll;queuePanel=panel;panel.center();panel.makeKeyAndOrderFront(nil)}
    func numberOfRows(in tableView:NSTableView)->Int{queue.count}
    func tableView(_ tableView:NSTableView,viewFor tableColumn:NSTableColumn?,row:Int)->NSView?{let t=NSTextField(labelWithString:(row==index ? "▶  ":"")+queue[row].lastPathComponent);t.textColor=row==index ? accent:.labelColor;t.lineBreakMode = .byTruncatingMiddle;return t}
    @objc func queueSelect(){if table.selectedRow>=0{play(table.selectedRow);window.makeKeyAndOrderFront(nil)}}
    func windowWillClose(_ notification:Notification){if previewMode{previousApp?.activate(options:[])}}
    @objc func receiveStream(_ event:NSAppleEventDescriptor, reply:NSAppleEventDescriptor) {
        guard let value=event.paramDescriptor(forKeyword: AEKeyword(0x2d2d2d2d))?.stringValue,
              let link=URLComponents(string:value), link.scheme=="mnplayer", link.host=="play",
              let address=link.queryItems?.first(where:{$0.name=="url"})?.value,
              let url=URL(string:address), ["http","https","rtsp","rtmp","udp","rtp","file"].contains(url.scheme?.lowercased() ?? "") else { return }
        if mp==nil { pending.append(url) } else { add([url]); window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps:true) }
    }
    func application(_ sender:NSApplication,open urls:[URL]){if mp==nil{pending += urls}else{add(urls);window.makeKeyAndOrderFront(nil)}}
    func applicationShouldHandleReopen(_ sender:NSApplication,hasVisibleWindows flag:Bool)->Bool{window?.makeKeyAndOrderFront(nil);return true}
    func applicationShouldTerminateAfterLastWindowClosed(_ sender:NSApplication)->Bool{true}
    func applicationShouldTerminate(_ sender:NSApplication)->NSApplication.TerminateReply {
        terminating=true;savePosition();timer?.invalidate();hideTimer?.invalidate();sleepTimer?.invalidate()
        guard mp != nil else{return .terminateNow}
        engineQueue.async {[self] in libvlc_media_player_stop(mp);libvlc_media_player_release(mp);libvlc_release(engine);DispatchQueue.main.async{NSApp.reply(toApplicationShouldTerminate:true)}}
        return .terminateLater
    }
    func buildMenu(){let bar=NSMenu();let appMenu=NSMenu();appMenu.addItem(item("MN Player Hakkında",#selector(about)));appMenu.addItem(item("Varsayılan video oynatıcı yap",#selector(setDefaults)));appMenu.addItem(item("Önceki varsayılanları geri yükle",#selector(restoreDefaults)));appMenu.addItem(.separator());appMenu.addItem(withTitle:"Çık",action:#selector(NSApplication.terminate(_:)),keyEquivalent:"q");let appItem=NSMenuItem();appItem.submenu=appMenu;bar.addItem(appItem)
        let file=NSMenu(title:"Dosya");file.addItem(item("Medya Aç…",#selector(openMedia),"o"));file.addItem(item("Bağlantı Aç…",#selector(openURL),"l"));file.addItem(item("Altyazı Ekle…",#selector(openCaptions),"s"));file.addItem(item("Oynatma Listesi",#selector(showQueue),"p"));let f=NSMenuItem(title:"Dosya",action:nil,keyEquivalent:"");f.submenu=file;bar.addItem(f)
        let play=NSMenu(title:"Oynatma");play.addItem(item("Oynat / Duraklat",#selector(togglePlay)));play.addItem(item("Sonraki",#selector(next)));play.addItem(item("Önceki",#selector(previous)));play.addItem(item("Tam Ekran",#selector(fullScreen),"f"));play.addItem(item("Mini Oynatıcı",#selector(mini),"i"));play.addItem(item("Kısayollar",#selector(shortcuts),"/"));let p=NSMenuItem(title:"Oynatma",action:nil,keyEquivalent:"");p.submenu=play;bar.addItem(p);NSApp.mainMenu=bar}
    @objc func about(){let a=NSAlert();a.messageText="MN Player " + (Bundle.main.object(forInfoDictionaryKey:"CFBundleShortVersionString") as? String ?? "Geliştirme");a.informativeText="Mavi Nokta Production\nVLC medya motoru\nLisanslar uygulama paketindeki Resources/Licenses klasöründedir.";a.beginSheetModal(for:window)}
    @objc func setDefaults(){Task { _ = await changeDefaults(restore:false) }}
    @objc func restoreDefaults(){Task { _ = await changeDefaults(restore:true) }}
    @MainActor func configureInstallation() async -> [String:Any] {
        // Merge the exact Finder menu shortcut without replacing other shortcuts.
        let domain="com.apple.finder" as CFString,key="NSUserKeyEquivalents" as CFString
        var shortcuts=CFPreferencesCopyValue(key,domain,kCFPreferencesCurrentUser,kCFPreferencesAnyHost) as? [String:String] ?? [:]
        if UserDefaults.standard.object(forKey:"finderShortcutBackup")==nil {
            UserDefaults.standard.set(["present":shortcuts["MN Player Önizleme"] != nil,"value":shortcuts["MN Player Önizleme"] ?? ""],forKey:"finderShortcutBackup")
        }
        shortcuts["MN Player Önizleme"]="^m"
        CFPreferencesSetValue(key,shortcuts as CFPropertyList,domain,kCFPreferencesCurrentUser,kCFPreferencesAnyHost)
        let synced=CFPreferencesSynchronize(domain,kCFPreferencesCurrentUser,kCFPreferencesAnyHost)
        let verified=(CFPreferencesCopyValue(key,domain,kCFPreferencesCurrentUser,kCFPreferencesAnyHost) as? [String:String])?["MN Player Önizleme"]=="^m"
        for provider in NSRunningApplication.runningApplications(withBundleIdentifier:"com.mavinokta.player.finder"){provider.terminate()}
        let registrar="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"
        var registration:[String:Int32]=[:]
        for path in [Bundle.main.bundleURL.path,Bundle.main.bundleURL.appendingPathComponent("Contents/Helpers/MN Finder.app").path] {
            let task=Process();task.executableURL=URL(fileURLWithPath:registrar);task.arguments=["-f",path];task.standardOutput=FileHandle.nullDevice;task.standardError=FileHandle.nullDevice
            do{try task.run();task.waitUntilExit();registration[path]=task.terminationStatus}catch{registration[path] = -1}
        }
        NSUpdateDynamicServices()
        let defaults=await changeDefaults(restore:false,silent:true)
        let success=synced && verified && registration.values.allSatisfy{$0==0} && !defaults.isEmpty && defaults.allSatisfy{$0["result"]=="OK"}
        let report:[String:Any]=["version":"2.2.0","shortcut":"Control + M","shortcutOK":synced && verified,"registration":registration,"videoDefaults":defaults,"success":success]
        let directory=FileManager.default.urls(for:.applicationSupportDirectory,in:.userDomainMask)[0].appendingPathComponent("MNPlayer")
        try? FileManager.default.createDirectory(at:directory,withIntermediateDirectories:true)
        if let data=try? JSONSerialization.data(withJSONObject:report,options:[.prettyPrinted,.sortedKeys]){try? data.write(to:directory.appendingPathComponent("installation.json"))}
        UserDefaults.standard.set(success,forKey:"initialSetupCompleted")
        return report
    }
    @MainActor func changeDefaults(restore:Bool,silent:Bool=false) async -> [[String:String]] {
        var types:[UTType]=[.movie,.video,.mpeg4Movie,.quickTimeMovie,.avi,.mpeg,.mpeg2Video]
        if let url=Bundle.main.url(forResource:"video-types",withExtension:"json"),let data=try? Data(contentsOf:url),let mapping=try? JSONDecoder().decode([String:String].self,from:data){types += mapping.values.compactMap{UTType($0)}}
        types += videoExtensions.compactMap{UTType(filenameExtension:$0)}
        var seen=Set<String>();types=types.filter{!$0.isDynamic && seen.insert($0.identifier).inserted}
        var backup=UserDefaults.standard.dictionary(forKey:"defaultBackup") as? [String:String] ?? [:]
        var results:[[String:String]]=[]
        for type in types {
            if !restore,backup[type.identifier]==nil,let old=NSWorkspace.shared.urlForApplication(toOpen:type){backup[type.identifier]=old.path}
            let destination=restore ? backup[type.identifier].map{URL(fileURLWithPath:$0)}:Bundle.main.bundleURL
            guard let destination else{continue}
            do {try await NSWorkspace.shared.setDefaultApplication(at:destination,toOpen:type);let actual=NSWorkspace.shared.urlForApplication(toOpen:type);results.append(["type":type.identifier,"result":actual?.standardizedFileURL==destination.standardizedFileURL ? "OK":"UNVERIFIED","application":actual?.path ?? ""])}catch{results.append(["type":type.identifier,"result":"ERROR","error":error.localizedDescription])}
        }
        UserDefaults.standard.set(backup,forKey:"defaultBackup")
        let dir=FileManager.default.urls(for:.applicationSupportDirectory,in:.userDomainMask)[0].appendingPathComponent("MNPlayer")
        try? FileManager.default.createDirectory(at:dir,withIntermediateDirectories:true)
        if let data=try? JSONSerialization.data(withJSONObject:results,options:[.prettyPrinted,.sortedKeys]){try? data.write(to:dir.appendingPathComponent(restore ? "restored-defaults.json":"defaults.json"))}
        let count=results.filter{$0["result"]=="OK"}.count
        if !silent,window != nil{notify("\(count) / \(results.count) dosya türü güncellendi")}
        return results
    }
}
let app=NSApplication.shared
if CommandLine.arguments.contains("--configure-install") {
    app.setActivationPolicy(.prohibited)
    let setup=AppDelegate()
    Task { @MainActor in
        let report=await setup.configureInstallation()
        if let data=try? JSONSerialization.data(withJSONObject:report,options:[.prettyPrinted,.sortedKeys]),let text=String(data:data,encoding:.utf8){print(text)}
        exit(report["success"] as? Bool == true ? 0:1)
    }
    app.run()
    exit(1)
}
let delegate=AppDelegate()
app.delegate=delegate
app.setActivationPolicy(delegate.previewMode ? .accessory:.regular)
app.run()
