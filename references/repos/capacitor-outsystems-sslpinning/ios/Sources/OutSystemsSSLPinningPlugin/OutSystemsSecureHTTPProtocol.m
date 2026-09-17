#import "OutSystemsSecureHTTPProtocol.h"
#import <os/log.h>

@interface OutSystemsSecureHTTPProtocol() <NSURLSessionDataDelegate, NSURLSessionTaskDelegate>

@property (atomic, strong, readwrite) NSThread *clientThread;
@property (atomic, copy, readwrite) NSArray* modes;
@property (nonatomic, strong) NSURLSessionDataTask *dataTask;

@end

@implementation OutSystemsSecureHTTPProtocol

static NSString * kRecursiveRequestFlagProperty = @"com.outsystems.OutSystemsSecureHTTPProtocol";

+ (BOOL)canInitWithRequest:(NSURLRequest *) request {
    
    BOOL shouldAccept;
    NSURL *url;
    NSString* scheme;
    
    shouldAccept = (request != nil);
    
    if(shouldAccept) {
        url = [request URL];
    }
    
    if(shouldAccept) {
        shouldAccept = ([self propertyForKey:kRecursiveRequestFlagProperty inRequest:request] == nil);
    }
    
    if(shouldAccept) {
        scheme = [[url scheme] lowercaseString];
        shouldAccept = (scheme != nil);
    }
    
    if(shouldAccept) {
        shouldAccept = [scheme isEqualToString:@"http"] || [scheme isEqualToString:@"https"];
    }
    
    if(shouldAccept) {
        if([NSURLProtocol propertyForKey:@"downloadResourceAsyncKEY" inRequest:request]) {
            os_log_debug(OS_LOG_DEFAULT, "OSSSLPinning - OutSystemsSecureHTTPProtocol loading from downloadResourceAsyncKEY");
            os_log_debug(OS_LOG_DEFAULT, "OSSSLPinning - Should accept request URL: %{public}@", request.URL.absoluteString);
        }
    }
    
    return shouldAccept;
}

+ (NSURLRequest *)canonicalRequestForRequest:(NSURLRequest *)request {
    return request;
}

- (void)startLoading {
    NSMutableURLRequest *recursiveRequest;
    NSMutableArray *calculatedModes;
    NSString *currentMode;
    
    // At this point we kick off the process of loading the URL via NSURLSession.
    // The thread that calls this method becomes the client thread.
    
    calculatedModes = [NSMutableArray array];
    [calculatedModes addObject:NSDefaultRunLoopMode];
    currentMode = [[NSRunLoop currentRunLoop] currentMode];
    if( (currentMode != nil) && ! [currentMode isEqual:NSDefaultRunLoopMode]) {
        [calculatedModes addObject:currentMode];
    }
    
    self.modes = calculatedModes;
    
    recursiveRequest = [[self request] mutableCopy];
    
    [[self class] setProperty:@YES forKey:kRecursiveRequestFlagProperty inRequest:recursiveRequest];
    
    self.clientThread = [NSThread currentThread];

    // Execute the request
    NSURLSession *session = [NSURLSession sessionWithConfiguration:NSURLSessionConfiguration.ephemeralSessionConfiguration delegate:self delegateQueue:nil];
    self.dataTask = [session dataTaskWithRequest:recursiveRequest];
    [self.dataTask resume];
}

- (void)stopLoading {
    if (self.dataTask != nil) {
        [self.dataTask cancel];
        self.dataTask = nil;
    } else {
        os_log_debug(OS_LOG_DEFAULT, "OSSSLPinning - Unable to stop loading");
    }
}


#pragma mark NSURLSessionDataDelegate

- (void)URLSession:(NSURLSession *)session dataTask:(NSURLSessionDataTask *)dataTask didReceiveResponse:(NSURLResponse *)response completionHandler:(void (^)(NSURLSessionResponseDisposition))completionHandler {
    [self.client URLProtocol:self didReceiveResponse:response cacheStoragePolicy:NSURLCacheStorageNotAllowed];
    completionHandler(NSURLSessionResponseAllow);
}

- (void)URLSession:(NSURLSession *)session dataTask:(NSURLSessionDataTask *)dataTask didReceiveData:(NSData *)data {
    [self.client URLProtocol:self didLoadData:data];
}

#pragma mark NSURLSessionTaskDelegate

- (void)URLSession:(NSURLSession *)session task:(NSURLSessionTask *)task didCompleteWithError:(NSError *)error {
    if (error == nil) {
        [self.client URLProtocolDidFinishLoading:self];
    } else {
        [self.client URLProtocol:self didFailWithError:error];
    }
}

- (void)URLSession:(NSURLSession *)session task:(NSURLSessionTask *)task willPerformHTTPRedirection:(NSHTTPURLResponse *)response newRequest:(NSURLRequest *)request completionHandler:(void (^)(NSURLRequest * _Nullable))completionHandler {
    if (response != nil) {
        [self.client URLProtocol:self wasRedirectedToRequest:request redirectResponse:response];
        [task cancel];
    }
    
    completionHandler(request);
}

@end
