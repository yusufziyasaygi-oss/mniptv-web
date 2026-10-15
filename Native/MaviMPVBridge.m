#import "MaviMPVBridge.h"
#import <OpenGL/gl3.h>
#import <OpenGL/OpenGL.h>
#import <QuartzCore/QuartzCore.h>
#include <mpv/client.h>
#include <mpv/render_gl.h>
#include <dlfcn.h>
#include <math.h>
#include <stdlib.h>

// Layer-hosted OpenGL surface (CAOpenGLLayer). Works inside SwiftUI's layer-backed
// view tree, unlike NSOpenGLView which renders black or corrupted there.
@interface MaviMPVLayer : CAOpenGLLayer
@property(nonatomic, assign) mpv_handle *handle;
@property(nonatomic, assign) int rendererError;
@property(nonatomic, assign) long frames;
@property(nonatomic, assign) long renderedFrames;
@property(nonatomic, assign) int lastViewportW;
@property(nonatomic, assign) int lastViewportH;
- (void)closeRenderer;
- (void)renderNow;
@end

static void *resolveGL(void *context, const char *name) { return dlsym(RTLD_DEFAULT, name); }
static void requestFrame(void *context);

@implementation MaviMPVLayer {
    mpv_render_context *_renderer;
    CGLContextObj _context;
    dispatch_queue_t _queue;
}
- (instancetype)init {
    if ((self = [super init])) {
        _queue = dispatch_queue_create("com.mavinokta.iptv.render", DISPATCH_QUEUE_SERIAL);
        self.asynchronous = NO;
        self.needsDisplayOnBoundsChange = YES;
        self.backgroundColor = NSColor.blackColor.CGColor;
        self.opaque = YES;
    }
    return self;
}
- (CGLPixelFormatObj)copyCGLPixelFormatForDisplayMask:(uint32_t)mask {
    CGLPixelFormatAttribute accelerated[] = {
        kCGLPFAOpenGLProfile, (CGLPixelFormatAttribute)kCGLOGLPVersion_3_2_Core,
        kCGLPFADoubleBuffer, kCGLPFAAccelerated, kCGLPFAAllowOfflineRenderers,
        kCGLPFAColorSize, (CGLPixelFormatAttribute)24, kCGLPFAAlphaSize, (CGLPixelFormatAttribute)8, 0
    };
    CGLPixelFormatAttribute fallback[] = {
        kCGLPFAOpenGLProfile, (CGLPixelFormatAttribute)kCGLOGLPVersion_3_2_Core,
        kCGLPFADoubleBuffer, kCGLPFAAllowOfflineRenderers,
        kCGLPFAColorSize, (CGLPixelFormatAttribute)24, kCGLPFAAlphaSize, (CGLPixelFormatAttribute)8, 0
    };
    CGLPixelFormatObj pixelFormat = NULL;
    GLint count = 0;
    CGLChoosePixelFormat(accelerated, &pixelFormat, &count);
    if (!pixelFormat) CGLChoosePixelFormat(fallback, &pixelFormat, &count);
    if (!pixelFormat) pixelFormat = [super copyCGLPixelFormatForDisplayMask:mask];
    return pixelFormat;
}
- (CGLContextObj)copyCGLContextForPixelFormat:(CGLPixelFormatObj)pixelFormat {
    CGLContextObj context = NULL;
    CGLCreateContext(pixelFormat, NULL, &context);
    if (!context) return [super copyCGLContextForPixelFormat:pixelFormat];
    GLint swap = 1;
    CGLSetParameter(context, kCGLCPSwapInterval, &swap);
    CGLEnable(context, kCGLCEMPEngine);
    return context;
}
- (void)ensureRendererInContext:(CGLContextObj)ctx {
    if (_renderer || !_handle) return;
    mpv_opengl_init_params gl = {.get_proc_address = resolveGL};
    mpv_render_param params[] = {
        {MPV_RENDER_PARAM_API_TYPE, MPV_RENDER_API_TYPE_OPENGL},
        {MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, &gl},
        {MPV_RENDER_PARAM_INVALID, NULL}
    };
    int result = mpv_render_context_create(&_renderer, _handle, params);
    if (result >= 0) {
        _context = ctx;
        CGLRetainContext(ctx);
        mpv_render_context_set_update_callback(_renderer, requestFrame, (__bridge void *)self);
    } else {
        _renderer = NULL;
        _rendererError = result;
    }
}
/// Called for every new mpv frame: draw right away on the render queue (as IINA does),
/// instead of waiting for Core Animation to schedule a redraw.
- (void)renderNow {
    dispatch_async(_queue, ^{ [self display]; });
}
- (void)display {
    [super display];
    [CATransaction flush];
}
- (BOOL)canDrawInCGLContext:(CGLContextObj)ctx pixelFormat:(CGLPixelFormatObj)pf
               forLayerTime:(CFTimeInterval)t displayTime:(const CVTimeStamp *)ts {
    return YES;
}
- (void)drawInCGLContext:(CGLContextObj)ctx pixelFormat:(CGLPixelFormatObj)pf
            forLayerTime:(CFTimeInterval)t displayTime:(const CVTimeStamp *)ts {
    CGLLockContext(ctx);
    CGLSetCurrentContext(ctx);
    [self ensureRendererInContext:ctx];
    GLint fbo = 0;
    glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &fbo);
    GLint viewport[4] = {0, 0, 0, 0};
    glGetIntegerv(GL_VIEWPORT, viewport);
    _frames++;
    _lastViewportW = viewport[2];
    _lastViewportH = viewport[3];
    if (_renderer && viewport[2] > 0 && viewport[3] > 0) {
        mpv_opengl_fbo target = {.fbo = (int)fbo, .w = viewport[2], .h = viewport[3]};
        int flip = 1, block = 0;
        mpv_render_param params[] = {
            {MPV_RENDER_PARAM_OPENGL_FBO, &target}, {MPV_RENDER_PARAM_FLIP_Y, &flip},
            {MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME, &block}, {MPV_RENDER_PARAM_INVALID, NULL}
        };
        mpv_render_context_render(_renderer, params);
        _renderedFrames++;
    } else {
        glClearColor(0, 0, 0, 1);
        glClear(GL_COLOR_BUFFER_BIT);
    }
    glFlush();
    [super drawInCGLContext:ctx pixelFormat:pf forLayerTime:t displayTime:ts];
    if (_renderer) mpv_render_context_report_swap(_renderer);
    CGLUnlockContext(ctx);
}
- (void)closeRenderer {
    dispatch_sync(_queue, ^{
        if (!self->_renderer) return;
        if (self->_context) { CGLLockContext(self->_context); CGLSetCurrentContext(self->_context); }
        mpv_render_context_set_update_callback(self->_renderer, NULL, NULL);
        mpv_render_context_free(self->_renderer);
        self->_renderer = NULL;
        if (self->_context) { CGLUnlockContext(self->_context); CGLReleaseContext(self->_context); self->_context = NULL; }
    });
}
- (void)dealloc { [self closeRenderer]; }
@end

static void requestFrame(void *context) {
    MaviMPVLayer *layer = (__bridge MaviMPVLayer *)context;
    [layer renderNow];
}

@interface MaviMPVSurface : NSView
@property(nonatomic, readonly) MaviMPVLayer *videoLayer;
@end
@implementation MaviMPVSurface
- (instancetype)init {
    if ((self = [super initWithFrame:NSMakeRect(0, 0, 800, 450)])) {
        // Layer-hosting (layer assigned before wantsLayer): AppKit never draws into or
        // replaces the layer contents, so only the OpenGL output is shown.
        MaviMPVLayer *layer = [[MaviMPVLayer alloc] init];
        layer.autoresizingMask = kCALayerWidthSizable | kCALayerHeightSizable;
        self.layer = layer;
        self.wantsLayer = YES;
        self.layerContentsRedrawPolicy = NSViewLayerContentsRedrawDuringViewResize;
    }
    return self;
}
- (MaviMPVLayer *)videoLayer { return (MaviMPVLayer *)self.layer; }
- (BOOL)acceptsFirstResponder { return NO; }
- (NSView *)hitTest:(NSPoint)point { return nil; }
- (void)viewDidChangeBackingProperties {
    [super viewDidChangeBackingProperties];
    self.layer.contentsScale = self.window.backingScaleFactor ?: 2.0;
    [self.layer setNeedsDisplay];
}
- (void)viewDidMoveToWindow {
    [super viewDidMoveToWindow];
    if (self.window) self.layer.contentsScale = self.window.backingScaleFactor ?: 2.0;
    [self.layer setNeedsDisplay];
}
@end

static id nodeObject(mpv_node *node) {
    switch (node->format) {
        case MPV_FORMAT_STRING: return node->u.string ? [NSString stringWithUTF8String:node->u.string] ?: @"" : @"";
        case MPV_FORMAT_FLAG: return @(node->u.flag);
        case MPV_FORMAT_INT64: return @(node->u.int64);
        case MPV_FORMAT_DOUBLE: return @(node->u.double_);
        case MPV_FORMAT_NODE_ARRAY: {
            NSMutableArray *array = [NSMutableArray array];
            for (int i = 0; i < node->u.list->num; i++) [array addObject:nodeObject(&node->u.list->values[i]) ?: NSNull.null];
            return array;
        }
        case MPV_FORMAT_NODE_MAP: {
            NSMutableDictionary *map = [NSMutableDictionary dictionary];
            for (int i = 0; i < node->u.list->num; i++) {
                NSString *key = [NSString stringWithUTF8String:node->u.list->keys[i]];
                if (key) map[key] = nodeObject(&node->u.list->values[i]) ?: NSNull.null;
            }
            return map;
        }
        default: return NSNull.null;
    }
}
@implementation MaviMPVBridge {
    mpv_handle *_handle;
    MaviMPVSurface *_video;
    NSInteger _initializationError;
    NSString *_initializationStage;
}
- (instancetype)init {
    if (!(self = [super init])) return nil;
    _initializationStage = @"create";
    _video = [[MaviMPVSurface alloc] init];
    _handle = mpv_create();
    if (!_video || !_handle) { _initializationError = MPV_ERROR_NOMEM; [self shutdown]; return self; }
    // Disable personal mpv config, scripts, console and on-disk watch-later URLs.
    const char *options[][2] = {
        {"config", "no"}, {"load-scripts", "no"}, {"terminal", "no"}, {"msg-level", "all=no"},
        {"vo", "libmpv"}, {"ao", "coreaudio"}, {"audio-device", "auto"}, {"audio-channels", "auto-safe"}, {"hwdec", "auto-safe"}, {"idle", "yes"}, {"keep-open", "no"},
        {"input-default-bindings", "no"}, {"input-vo-keyboard", "no"}, {"osc", "no"},
        {"osd-level", "0"}, {"save-position-on-quit", "no"}, {"sub-auto", "no"},
        {"audio-file-auto", "no"}, {"ytdl", "no"}, {"network-timeout", "15"}, {"cache", "yes"},
        {"tls-verify", "yes"}, {"cache-on-disk", "no"}, {"cache-secs", "120"}, {"demuxer-max-bytes", "256MiB"}, {"demuxer-max-back-bytes", "512MiB"}, {"demuxer-readahead-secs", "60"}
    };
    for (size_t i = 0; i < sizeof(options)/sizeof(options[0]); i++) {
        _initializationStage = [@"option-" stringByAppendingString:[NSString stringWithUTF8String:options[i][0]]];
        int result = mpv_set_option_string(_handle, options[i][0], options[i][1]);
        if (result < 0) { _initializationError = result; [self shutdown]; return self; }
    }
    const char *hwOverride = getenv("MN_HWDEC");
    if (hwOverride && *hwOverride) mpv_set_option_string(_handle, "hwdec", hwOverride);
    NSString *certificates = [NSBundle.mainBundle pathForResource:@"CACertificates" ofType:@"pem"];
    if (!certificates && [[NSFileManager defaultManager] fileExistsAtPath:@"/etc/ssl/cert.pem"]) certificates = @"/etc/ssl/cert.pem";
    if (certificates) {
        int caResult = mpv_set_option_string(_handle, "tls-ca-file", certificates.UTF8String);
        if (caResult < 0) { _initializationError = caResult; [self shutdown]; return self; }
    }
    _initializationStage = @"initialize";
    int result = mpv_initialize(_handle);
    if (result >= 0) { _initializationStage = @"render"; _video.videoLayer.handle = _handle; [_video.videoLayer renderNow]; }
    if (result < 0) { _initializationError = result; [self shutdown]; }
    return self;
}
- (NSView *)surface { return _video ?: [[NSView alloc] init]; }
- (BOOL)ready { return _handle != NULL && _initializationError == 0; }
- (NSInteger)initializationError { return _initializationError; }
- (NSString *)initializationStage { return _initializationStage ?: @"unknown"; }
- (int)loadAddress:(NSString *)address {
    if (!_handle) return MPV_ERROR_UNINITIALIZED;
    // argv avoids command-string parsing: URL credentials never pass through a shell.
    const char *args[] = {"loadfile", address.UTF8String, "replace", NULL};
    return mpv_command_async(_handle, 1, args);
}
- (NSArray<NSDictionary<NSString *,id> *> *)drainEvents {
    NSMutableArray *result = [NSMutableArray array];
    if (!_handle) return result;
    for (int i = 0; i < 128; i++) {
        mpv_event *event = mpv_wait_event(_handle, 0);
        if (event->event_id == MPV_EVENT_NONE) break;
        if (event->event_id == MPV_EVENT_FILE_LOADED) [result addObject:@{@"kind": @"loaded"}];
        if (event->event_id == MPV_EVENT_END_FILE && event->data) {
            mpv_event_end_file *end = event->data;
            if (end->reason == MPV_END_FILE_REASON_ERROR) [result addObject:@{@"kind": @"failed", @"code": @(end->error)}];
            else if (end->reason == MPV_END_FILE_REASON_EOF) [result addObject:@{@"kind": @"ended"}];
        }
        if (event->event_id == MPV_EVENT_COMMAND_REPLY && event->reply_userdata == 1 && event->error < 0) [result addObject:@{@"kind": @"failed", @"code": @(event->error)}];
    }
    return result;
}
- (NSArray<NSDictionary<NSString *,id> *> *)tracks {
    if (!_handle) return @[];
    mpv_node node = {0};
    if (mpv_get_property(_handle, "track-list", MPV_FORMAT_NODE, &node) < 0) return @[];
    id object = nodeObject(&node); mpv_free_node_contents(&node);
    return [object isKindOfClass:NSArray.class] ? object : @[];
}
- (double)numberForProperty:(NSString *)name {
    double value = NAN;
    if (_handle) mpv_get_property(_handle, name.UTF8String, MPV_FORMAT_DOUBLE, &value);
    return value;
}
- (BOOL)flagForProperty:(NSString *)name {
    int value = 0;
    if (_handle) mpv_get_property(_handle, name.UTF8String, MPV_FORMAT_FLAG, &value);
    return value != 0;
}
- (NSString *)textForProperty:(NSString *)name {
    if (!_handle) return @"";
    char *value = mpv_get_property_string(_handle, name.UTF8String);
    if (!value) return @"";
    NSString *result = [NSString stringWithUTF8String:value] ?: @"";
    mpv_free(value);
    return result;
}
- (int)setNumber:(double)value property:(NSString *)name {
    if (!_handle || !isfinite(value)) return MPV_ERROR_INVALID_PARAMETER;
    return mpv_set_property(_handle, name.UTF8String, MPV_FORMAT_DOUBLE, &value);
}
- (int)setText:(NSString *)value property:(NSString *)name {
    if (!_handle) return MPV_ERROR_UNINITIALIZED;
    const char *text = value.UTF8String;
    return mpv_set_property(_handle, name.UTF8String, MPV_FORMAT_STRING, &text);
}
- (int)reloadAudio {
    if (!_handle) return MPV_ERROR_UNINITIALIZED;
    const char *args[] = {"ao-reload", NULL};
    return mpv_command(_handle, args);
}
- (int)stopPlayback {
    if (!_handle) return MPV_ERROR_UNINITIALIZED;
    const char *args[] = {"stop", NULL};
    return mpv_command_async(_handle, 0, args);
}
- (int)seekTo:(double)seconds absolute:(BOOL)absolute {
    if (!_handle || !isfinite(seconds)) return MPV_ERROR_INVALID_PARAMETER;
    NSString *position = [NSString stringWithFormat:@"%.6f", seconds];
    const char *args[] = {"seek", position.UTF8String, absolute ? "absolute+exact" : "relative+exact", NULL};
    return mpv_command_async(_handle, 3, args);
}
- (NSString *)diagnostics {
    MaviMPVLayer *l = _video.videoLayer;
    NSRect b = _video.bounds;
    return [NSString stringWithFormat:@"layer=%@ frames=%ld rendered=%ld rerr=%d vp=%dx%d bounds=%.0fx%.0f window=%d hidden=%d super=%@",
            NSStringFromClass(l.class), l.frames, l.renderedFrames, l.rendererError, l.lastViewportW, l.lastViewportH,
            b.size.width, b.size.height, _video.window != nil, _video.isHiddenOrHasHiddenAncestor,
            NSStringFromClass(_video.superview.class)];
}
- (void)shutdown {
    if (_handle) {
        const char *stop[] = {"stop", NULL};
        mpv_command_async(_handle, 0, stop);
    }
    [_video.videoLayer closeRenderer];
    _video.videoLayer.handle = NULL;
    mpv_handle *old = _handle; _handle = NULL;
    // Network teardown must not block AppKit's main thread.
    if (old) dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{ mpv_terminate_destroy(old); });
}
- (void)dealloc { [self shutdown]; }
@end
