#import "PURBufferedOutput.h"
#import "PURLogStore.h"
#import "PURLog.h"

NSString * const PURBufferedOutputSettingsLogLimitKey = @"BufferedOutputLogLimit";
NSString * const PURBufferedOutputSettingsFlushIntervalKey = @"BufferedOutputFlushInterval";
NSString * const PURBufferedOutputSettingsMaxRetryCountKey = @"BufferedOutputMaxRetryCount";

NSString * const PURBufferedOutputDidStartNotification = @"PURBufferedOutputDidStartNotification";
NSString * const PURBufferedOutputDidResumeNotification = @"PURBufferedOutputDidResumeNotification";
NSString * const PURBufferedOutputDidFlushNotification = @"PURBufferedOutputDidFlushNotification";
NSString * const PURBufferedOutputDidTryWriteChunkNotification = @"PURBufferedOutputDidTryWriteChunkNotification";
NSString * const PURBufferedOutputDidSuccessWriteChunkNotification = @"PURBufferedOutputDidSuccessWriteChunkNotification";
NSString * const PURBufferedOutputDidRetryWriteChunkNotification = @"PURBufferedOutputDidRetryWriteChunkNotification";

NSUInteger PURBufferedOutputDefaultLogLimit = 5;
NSTimeInterval PURBufferedOutputDefaultFlushInterval = 10;
NSUInteger PURBufferedOutputDefaultMaxRetryCount = 3;

@implementation PURBufferedOutputChunk

- (instancetype)initWithLogs:(NSArray<PURLog *> *)logs
{
    self = [super init];
    _logs = logs;
    _retryCount = 0;

    return self;
}

@end

@interface PURBufferedOutput ()

@property (nonatomic) NSUInteger logLimit;
@property (nonatomic) NSTimeInterval flushInterval;
@property (nonatomic) NSUInteger maxRetryCount;
@property (nonatomic) CFAbsoluteTime recentFlushTime;
@property (nonatomic) NSTimer *timer;

@end

@implementation PURBufferedOutput

- (void)dealloc
{
    [self.timer invalidate];
}

- (void)setUpTimer
{
    [self.timer invalidate];
    if(self.flushInterval <= 0){
        self.flushInterval = 1.0;
    }
    
    self.timer = [NSTimer timerWithTimeInterval:self.flushInterval
                                             target:self
                                           selector:@selector(tick)
                                           userInfo:nil
                                            repeats:NO];
    [self.timer setTolerance:1.0];
    [[NSRunLoop currentRunLoop] addTimer:self.timer forMode:NSRunLoopCommonModes];
}

- (void)configure:(NSDictionary<NSString *, id> *)settings
{
    [super configure:settings];

    id value;

    value = settings[PURBufferedOutputSettingsLogLimitKey];
    self.logLimit = value ? [value unsignedIntegerValue] : PURBufferedOutputDefaultLogLimit;

    value = settings[PURBufferedOutputSettingsFlushIntervalKey];
    self.flushInterval = value ? [value unsignedIntegerValue] : PURBufferedOutputDefaultFlushInterval;

    value = settings[PURBufferedOutputSettingsMaxRetryCountKey];
    self.maxRetryCount = value ? [value unsignedIntegerValue] : PURBufferedOutputDefaultMaxRetryCount;

    self.buffer = [NSMutableArray new];
}

- (void)start
{
    [super start];

    [self.buffer removeAllObjects];
    [self retrieveLogs:^(NSArray<PURLog *> * _Nonnull logs){
        [[NSNotificationCenter defaultCenter] postNotificationName:PURBufferedOutputDidStartNotification object:self];

        if (![self.timer isValid]) {
            return;
        }
        [self.buffer addObjectsFromArray:logs];
        [self truncateBufferedLogs];
        [self flush];
    }];

    [self setUpTimer];
}

- (void)resume
{
    [super resume];

    [self.buffer removeAllObjects];
    [self retrieveLogs:^(NSArray<PURLog *> * _Nonnull logs){
        [[NSNotificationCenter defaultCenter] postNotificationName:PURBufferedOutputDidResumeNotification object:self];

        if (![self.timer isValid]) {
            return;
        }
        [self.buffer addObjectsFromArray:logs];
        [self flush];
    }];

    [self setUpTimer];
}

- (void)suspend
{
    [self.timer invalidate];
    
    [super suspend];
}

- (void)tick
{
    [self flush];
}

- (void)retrieveLogs:(PURLogStoreRetrieveCompletionBlock)completion
{
    [self.buffer removeAllObjects];
    [self.logStore retrieveLogsForOutput:self
                              completion:completion];
}

- (void)emitLog:(PURLog *)log
{
    [self.buffer addObject:log];
    [self.logStore addLog:log forOutput:self completion:^{
        if ([self.buffer count] >= self.logLimit) {
            [self setUpTimer];
        }
    }];
    
    //When new logger is emitted, setup the timer if it is invalid
    if(![self.timer isValid]){
        [self setUpTimer];
    }
}

- (void)flush
{
    self.recentFlushTime = CFAbsoluteTimeGetCurrent();

    if ([self.buffer count] == 0) {
        return;
    }

    NSUInteger logCount = MIN([self.buffer count], self.logLimit);
    NSIndexSet *indexSet = [NSIndexSet indexSetWithIndexesInRange:NSMakeRange(0, logCount)];
    NSArray<PURLog *> *flushLogs = [self.buffer objectsAtIndexes:indexSet];
    [self.buffer removeObjectsAtIndexes:indexSet];

    PURBufferedOutputChunk *chunk = [[PURBufferedOutputChunk alloc] initWithLogs:flushLogs];
    [self callWriteChunk:chunk];

    [[NSNotificationCenter defaultCenter] postNotificationName:PURBufferedOutputDidFlushNotification object:self];
}

- (void)callWriteChunk:(PURBufferedOutputChunk *)chunk
{
    [self writeChunk:chunk
          completion:^(BOOL success){
              [[NSNotificationCenter defaultCenter] postNotificationName:PURBufferedOutputDidTryWriteChunkNotification object:self];

              if (success) {
                  [self.logStore removeLogs:chunk.logs forOutput:self completion:^{
                      [[NSNotificationCenter defaultCenter] postNotificationName:PURBufferedOutputDidSuccessWriteChunkNotification object:self];
                      //Ensure that we setUp the timer on the main thread
                      dispatch_async(dispatch_get_main_queue(), ^{
                          if([self.buffer count] > 0 && ![self.timer isValid]){
                              [self setUpTimer];
                          }
                      });
                  }];
                  return;
              }
              
              chunk.retryCount++;
              if (chunk.retryCount <= self.maxRetryCount) {
                  int64_t delay = 2.0 * pow(2, chunk.retryCount - 1);
                  dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(delay * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
                      [[NSNotificationCenter defaultCenter] postNotificationName:PURBufferedOutputDidRetryWriteChunkNotification object:self];
                      
                      [self callWriteChunk:chunk];
                  });
              }else {
                  // In case of fail, add chunk to the buffer again
                  [self.buffer addObjectsFromArray:chunk.logs];
                  
                  //Ensure that we setUp the timer on the main thread
                  dispatch_async(dispatch_get_main_queue(), ^{
                      if([self.buffer count] > 0 && ![self.timer isValid]){
                          [self setUpTimer];
                      }
                  });
              }
          }];
}

- (void)writeChunk:(PURBufferedOutputChunk *)chunk completion:(void (^)(BOOL))completion
{
    completion(YES);
}

- (void) truncateBufferedLogs {

}

@end
