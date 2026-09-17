//
//  LatencyDetection.m
//  OutSystems
//
//  Created by Vitor Oliveira on 03/11/15.
//
//

#import "LatencyDetection.h"

@interface LatencyDetection(){
    NSDate* _dateReference;
}
@property(nonatomic, strong) void(^resultCallback)(NSString* latency);
    
@end

@implementation LatencyDetection

+(void)pingHostname:(NSString*)hostName andResultCallback:(void(^)(NSString* latency))result
{
    static LatencyDetection* latencyDetection = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        latencyDetection = [[LatencyDetection alloc] init];
    });
    
    //ping hostname
    [latencyDetection pingHostname:hostName andResultCallBlock:result];
}

-(void)pingHostname:(NSString*)hostName andResultCallBlock:(void(^)(NSString* latency))result
{
    _resultCallback = result;
    
    NSURL *url = [NSURL URLWithString:[NSString stringWithFormat:@"https://%@/servicecenter/_ping.html", hostName]];
    NSURLRequest *request = [NSURLRequest requestWithURL:url
                                             cachePolicy:NSURLRequestReloadIgnoringCacheData
                                         timeoutInterval:60.0];
    
     _dateReference = [NSDate date];
    [NSURLConnection connectionWithRequest:request delegate:self];
}

#pragma mark - NSURLConnection Delegate
- (void)connection:(NSURLConnection *)connection didReceiveResponse:(NSURLResponse *)response {
}

- (void)connection:(NSURLConnection *)connection didReceiveData:(NSData *)data {
    NSDate *end=[NSDate date];
    double latency = [end timeIntervalSinceDate:_dateReference] * 1000;//get in miliseconds
    _resultCallback([NSString stringWithFormat:@"%.f", latency]);
}

- (void)connection:(NSURLConnection *)connection didFailWithError:(NSError *)error {
    _resultCallback(nil);
}

- (void)connectionDidFinishLoading:(NSURLConnection *)connection {
}

@end
