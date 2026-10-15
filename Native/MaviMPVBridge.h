#import <Cocoa/Cocoa.h>
NS_ASSUME_NONNULL_BEGIN
// The only native playback backend. Public interface contains no mpv C structs.
@interface MaviMPVBridge : NSObject
@property(nonatomic, readonly) NSView *surface;
@property(nonatomic, readonly) BOOL ready;
@property(nonatomic, readonly) NSInteger initializationError;
@property(nonatomic, readonly) NSString *initializationStage;
- (int)loadAddress:(NSString *)address NS_SWIFT_NAME(load(address:));
- (NSArray<NSDictionary<NSString *, id> *> *)drainEvents NS_SWIFT_NAME(drainEvents());
- (NSArray<NSDictionary<NSString *, id> *> *)tracks NS_SWIFT_NAME(tracks());
- (double)numberForProperty:(NSString *)name NS_SWIFT_NAME(number(_:));
- (BOOL)flagForProperty:(NSString *)name NS_SWIFT_NAME(flag(_:));
- (NSString *)textForProperty:(NSString *)name NS_SWIFT_NAME(text(_:));
- (int)setNumber:(double)value property:(NSString *)name NS_SWIFT_NAME(set(number:property:));
- (int)setText:(NSString *)value property:(NSString *)name NS_SWIFT_NAME(set(text:property:));
- (int)seekTo:(double)seconds absolute:(BOOL)absolute NS_SWIFT_NAME(seek(seconds:absolute:));
- (int)reloadAudio NS_SWIFT_NAME(reloadAudio());
- (int)stopPlayback NS_SWIFT_NAME(stopPlayback());
- (void)shutdown NS_SWIFT_NAME(shutdown());
@end
NS_ASSUME_NONNULL_END
