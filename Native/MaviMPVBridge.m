#import "MaviMPVBridge.h"
#import <OpenGL/gl3.h>
#include <mpv/client.h>
#include <mpv/render_gl.h>
#include <dlfcn.h>
#include <math.h>

@interface MaviMPVSurface : NSOpenGLView
- (int)attach:(mpv_handle *)handle;
- (void)closeRenderer;
@end
static void *resolveGL(void *context, const char *name) { return dlsym(RTLD_DEFAULT, name); }
static void requestFrame(void *context) {
    MaviMPVSurface *view = (__bridge MaviMPVSurface *)context;
    dispatch_async(dispatch_get_main_queue(), ^{ view.needsDisplay = YES; });
}
@implementation MaviMPVSurface {
    mpv_render_context *_renderer;
}
- (instancetype)init {
    NSOpenGLPixelFormatAttribute attrs[] = {
        NSOpenGLPFAOpenGLProfile, NSOpenGLProfileVersion3_2Core,
        NSOpenGLPFAAccelerated, NSOpenGLPFADoubleBuffer,
        NSOpenGLPFAColorSize, 24, NSOpenGLPFAAlphaSize, 8, 0
    };
    NSOpenGLPixelFormat *format = [[NSOpenGLPixelFormat alloc] initWithAttributes:attrs];
    if (!format) {
        // No hardware renderer (virtual machines, some external displays): fall back to any 3.2 core renderer.
        NSOpenGLPixelFormatAttribute fallback[] = {
            NSOpenGLPFAOpenGLProfile, NSOpenGLProfileVersion3_2Core,
            NSOpenGLPFADoubleBuffer, NSOpenGLPFAColorSize, 24, NSOpenGLPFAAlphaSize, 8, 0
        };
        format = [[NSOpenGLPixelFormat alloc] initWithAttributes:fallback];
    }
    if (!format) return nil;
    self = [super initWithFrame:NSMakeRect(0, 0, 800, 450) pixelFormat:format];
    if (self) self.wantsBestResolutionOpenGLSurface = YES;
    return self;
}
- (int)attach:(mpv_handle *)handle {
    [self.openGLContext makeCurrentContext];
    if (!self.openGLContext) return MPV_ERROR_UNSUPPORTED;
    GLint swap = 1; [self.openGLContext setValues:&swap forParameter:NSOpenGLCPSwapInterval];
    mpv_opengl_init_params gl = {.get_proc_address = resolveGL};
    mpv_render_param params[] = {
        {MPV_RENDER_PARAM_API_TYPE, MPV_RENDER_API_TYPE_OPENGL},
        {MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, &gl},
        {MPV_RENDER_PARAM_INVALID, NULL}
    };
    int result = mpv_render_context_create(&_renderer, handle, params);
    if (result >= 0) mpv_render_context_set_update_callback(_renderer, requestFrame, (__bridge void *)self);
    return result;
}
- (void)viewDidMoveToWindow {
    [super viewDidMoveToWindow];
    [self.openGLContext update]; self.needsDisplay = YES;
}
- (void)reshape { [super reshape]; [self.openGLContext update]; self.needsDisplay = YES; }
- (void)drawRect:(NSRect)dirtyRect {
    [self.openGLContext makeCurrentContext];
    NSRect pixels = [self convertRectToBacking:self.bounds];
    if (pixels.size.width < 1 || pixels.size.height < 1) return;
    glViewport(0, 0, (GLsizei)pixels.size.width, (GLsizei)pixels.size.height);
    glClearColor(0, 0, 0, 1); glClear(GL_COLOR_BUFFER_BIT);
    if (_renderer) {
        mpv_render_context_update(_renderer);
        mpv_opengl_fbo fbo = {.fbo = 0, .w = (int)pixels.size.width, .h = (int)pixels.size.height};
        int flip = 1, block = 0;
        mpv_render_param params[] = {
            {MPV_RENDER_PARAM_OPENGL_FBO, &fbo}, {MPV_RENDER_PARAM_FLIP_Y, &flip},
            {MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME, &block}, {MPV_RENDER_PARAM_INVALID, NULL}
        };
        mpv_render_context_render(_renderer, params);
    }
    [self.openGLContext flushBuffer];
    if (_renderer) mpv_render_context_report_swap(_renderer);
}
- (void)closeRenderer {
    if (!_renderer) return;
    [self.openGLContext makeCurrentContext];
    mpv_render_context_set_update_callback(_renderer, NULL, NULL);
    mpv_render_context_free(_renderer); _renderer = NULL;
}
- (void)dealloc { [self closeRenderer]; }
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
    NSString *certificates = [NSBundle.mainBundle pathForResource:@"CACertificates" ofType:@"pem"];
    if (!certificates && [[NSFileManager defaultManager] fileExistsAtPath:@"/etc/ssl/cert.pem"]) certificates = @"/etc/ssl/cert.pem";
    if (certificates) {
        int caResult = mpv_set_option_string(_handle, "tls-ca-file", certificates.UTF8String);
        if (caResult < 0) { _initializationError = caResult; [self shutdown]; return self; }
    }
    _initializationStage = @"initialize";
    int result = mpv_initialize(_handle);
    if (result >= 0) { _initializationStage = @"render"; result = [_video attach:_handle]; }
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
- (void)shutdown {
    if (_handle) {
        const char *stop[] = {"stop", NULL};
        mpv_command_async(_handle, 0, stop);
    }
    [_video closeRenderer];
    mpv_handle *old = _handle; _handle = NULL;
    // Network teardown must not block AppKit's main thread.
    if (old) dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{ mpv_terminate_destroy(old); });
}
- (void)dealloc { [self shutdown]; }
@end
