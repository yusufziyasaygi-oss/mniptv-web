#import <QuickLookThumbnailing/QuickLookThumbnailing.h>
#import <CoreGraphics/CoreGraphics.h>
#import <ImageIO/ImageIO.h>
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/imgutils.h>
#include <libavutil/time.h>
#include <libswscale/swscale.h>
#include <math.h>

static int timedOut(void *opaque) {
    return av_gettime_relative() >= *(int64_t *)opaque;
}

// The decoder emits frames in presentation order, including clips with B frames.
// Only EOF flushes the decoder, so truncated packets never become a substitute image.
static BOOL readFirstFrame(AVFormatContext *input, int stream, AVCodecContext *decoder,
                           AVFrame *frame, int64_t deadline) {
    AVPacket *packet = av_packet_alloc();
    if (!packet) return NO;
    BOOL found = NO;
    while (!timedOut(&deadline)) {
        int status = av_read_frame(input, packet);
        if (status >= 0 && packet->stream_index != stream) {
            av_packet_unref(packet);
            continue;
        }
        BOOL eof = status == AVERROR_EOF;
        if (status < 0 && !eof) break;
        status = avcodec_send_packet(decoder, eof ? NULL : packet);
        av_packet_unref(packet);
        if (status < 0) break;
        status = avcodec_receive_frame(decoder, frame);
        if (status == 0) { found = YES; break; }
        if (eof || status != AVERROR(EAGAIN)) break;
    }
    av_packet_free(&packet);
    return found;
}

static CGImageRef renderFrame(AVFrame *frame, AVRational sar, CGSize pixelLimit) {
    if (frame->width <= 0 || frame->height <= 0) return NULL;
    double aspect = (double)frame->width / frame->height;
    if (sar.num > 0 && sar.den > 0) aspect *= av_q2d(sar);
    int width = MAX(1, (int)llround(fmin(pixelLimit.width, pixelLimit.height * aspect)));
    int height = MAX(1, (int)llround(fmin(pixelLimit.height, pixelLimit.width / aspect)));
    struct SwsContext *scaler = sws_getContext(frame->width, frame->height, frame->format,
        width, height, AV_PIX_FMT_RGBA, SWS_BILINEAR, NULL, NULL, NULL);
    if (!scaler) return NULL;
    const int *coeff = sws_getCoefficients(frame->colorspace == AVCOL_SPC_UNSPECIFIED
        ? (frame->height >= 720 ? SWS_CS_ITU709 : SWS_CS_ITU601) : frame->colorspace);
    sws_setColorspaceDetails(scaler, coeff, frame->color_range == AVCOL_RANGE_JPEG,
                            coeff, 1, 0, 1 << 16, 1 << 16);
    uint8_t *planes[4] = {0};
    int strides[4] = {0};
    CGImageRef image = NULL;
    if (av_image_alloc(planes, strides, width, height, AV_PIX_FMT_RGBA, 32) >= 0 &&
        sws_scale(scaler, (const uint8_t * const *)frame->data, frame->linesize,
                  0, frame->height, planes, strides) == height) {
        NSData *pixels = [NSData dataWithBytes:planes[0] length:(NSUInteger)strides[0] * height];
        CGDataProviderRef provider = CGDataProviderCreateWithCFData((__bridge CFDataRef)pixels);
        CGColorSpaceRef rgb = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        image = CGImageCreate(width, height, 8, 32, strides[0], rgb,
                              kCGImageAlphaNoneSkipLast | kCGBitmapByteOrder32Big,
                              provider, NULL, false, kCGRenderingIntentDefault);
        CGColorSpaceRelease(rgb);
        CGDataProviderRelease(provider);
    }
    av_freep(&planes[0]);
    sws_freeContext(scaler);
    return image;
}

static CGImageRef firstImage(NSURL *url, CGSize pixelLimit) {
    int64_t deadline = av_gettime_relative() + 8 * AV_TIME_BASE;
    AVFormatContext *input = avformat_alloc_context();
    if (!input) return NULL;
    input->interrupt_callback = (AVIOInterruptCB){timedOut, &deadline};
    input->max_analyze_duration = AV_TIME_BASE;
    input->probesize = 4 * 1024 * 1024;
    AVCodecContext *decoder = NULL;
    AVFrame *frame = NULL;
    CGImageRef image = NULL;
    if (avformat_open_input(&input, url.fileSystemRepresentation, av_find_input_format("mxf"), NULL) < 0 ||
        avformat_find_stream_info(input, NULL) < 0) goto done;
    const AVCodec *codec = NULL;
    int stream = av_find_best_stream(input, AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
    if (stream < 0) goto done;
    decoder = avcodec_alloc_context3(codec);
    if (!decoder || avcodec_parameters_to_context(decoder, input->streams[stream]->codecpar) < 0) goto done;
    decoder->thread_count = 1;
    decoder->max_pixels = 8192LL * 4608;
    if (avcodec_open2(decoder, codec, NULL) < 0) goto done;
    frame = av_frame_alloc();
    if (frame && readFirstFrame(input, stream, decoder, frame, deadline)) {
        image = renderFrame(frame, av_guess_sample_aspect_ratio(input, input->streams[stream], frame), pixelLimit);
    }
done:
    av_frame_free(&frame);
    avcodec_free_context(&decoder);
    avformat_close_input(&input);
    return image;
}

// Quick Look consumes a file reply after the handler returns. Give concurrent
// requests separate files and clear older replies when the extension is reused.
static NSURL *replyDirectory(void) {
    NSFileManager *manager = NSFileManager.defaultManager;
    NSURL *directory = [manager.temporaryDirectory URLByAppendingPathComponent:@"MXFReplies" isDirectory:YES];
    if (![manager createDirectoryAtURL:directory withIntermediateDirectories:YES attributes:nil error:nil]) return nil;
    static NSTimeInterval nextCleanup;
    @synchronized (manager) {
        NSTimeInterval now = NSDate.timeIntervalSinceReferenceDate;
        if (now >= nextCleanup) {
            nextCleanup = now + 3600;
            NSArray *files = [manager contentsOfDirectoryAtURL:directory
                includingPropertiesForKeys:@[NSURLContentModificationDateKey] options:0 error:nil];
            for (NSURL *file in files) {
                NSDate *date = nil;
                if ([file getResourceValue:&date forKey:NSURLContentModificationDateKey error:nil] &&
                    date && date.timeIntervalSinceReferenceDate < now - 3600) {
                    [manager removeItemAtURL:file error:nil];
                }
            }
        }
    }
    return directory;
}

@interface MNMXFThumbnailProvider : QLThumbnailProvider
@end

@implementation MNMXFThumbnailProvider
- (void)provideThumbnailForFileRequest:(QLFileThumbnailRequest *)request
                     completionHandler:(void (^)(QLThumbnailReply *, NSError *))handler {
    NSLog(@"MNMXFThumbnailProvider: request received");
    CGSize maximum = request.maximumSize;
    CGSize pixels = CGSizeMake(fmin(1536, maximum.width * request.scale),
                               fmin(1536, maximum.height * request.scale));
    CGImageRef image = NULL;
    if (request.fileURL.isFileURL && [request.fileURL.pathExtension.lowercaseString isEqualToString:@"mxf"] &&
        pixels.width > 0 && pixels.height > 0) {
        image = firstImage(request.fileURL, pixels);
    }
    if (!image) {
        NSLog(@"MNMXFThumbnailProvider: first frame unavailable");
        handler(nil, [NSError errorWithDomain:NSCocoaErrorDomain code:NSFileReadCorruptFileError userInfo:nil]);
        return;
    }
    NSURL *directory = replyDirectory();
    NSString *name = [NSUUID.UUID.UUIDString stringByAppendingPathExtension:@"png"];
    NSURL *file = [directory URLByAppendingPathComponent:name];
    CGImageDestinationRef destination = file ? CGImageDestinationCreateWithURL(
        (__bridge CFURLRef)file, CFSTR("public.png"), 1, NULL) : NULL;
    BOOL written = NO;
    if (destination) {
        CGImageDestinationAddImage(destination, image, NULL);
        written = CGImageDestinationFinalize(destination);
        CFRelease(destination);
    }
    CGImageRelease(image);
    if (!written) {
        if (file) [NSFileManager.defaultManager removeItemAtURL:file error:nil];
        handler(nil, [NSError errorWithDomain:NSCocoaErrorDomain code:NSFileWriteUnknownError userInfo:nil]);
        return;
    }
    NSLog(@"MNMXFThumbnailProvider: first frame generated");
    handler([QLThumbnailReply replyWithImageFileURL:file], nil);
}
@end
