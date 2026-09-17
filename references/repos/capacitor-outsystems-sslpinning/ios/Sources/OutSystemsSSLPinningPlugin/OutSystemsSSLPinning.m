#import "OutSystemsSecureHTTPProtocol.h"
#import "OutSystemsSSLPinning.h"
#import <Foundation/Foundation.h>
#import <objc/runtime.h>
#import <Capacitor/Capacitor.h>
#import <Capacitor/Capacitor-Swift.h>
#import <Capacitor/CAPBridgedPlugin.h>
#import <Capacitor/CAPBridgedJSTypes.h>
#import <TrustKit/TrustKit.h>

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wprotocol"
// suppressing warnings of the type: "Class 'OutSystemsSSLPinningPlugin' does not conform to protocol 'CAPBridgedPlugin'"
// protocol conformance for this class is implemented by a macro and clang isn't detecting that
@implementation OutSystemsSSLPinningPlugin

- (void)load
{
  [NSURLProtocol registerClass: [OutSystemsSecureHTTPProtocol class]];
}

#pragma mark Plugin interface

- (void)checkCertificate:(CAPPluginCall *)call
{
    NSString *url = [call getString:@"url" defaultValue:@""];
    if (url != nil || [url length] == 0) {
        NSMutableURLRequest *request = [[NSMutableURLRequest alloc] init];
        [request setHTTPMethod:@"GET"];
        [request setURL:[NSURL URLWithString:url]];
        
        NSURLSessionDataTask *dataTask = [NSURLSession.sharedSession dataTaskWithRequest:request completionHandler:^(NSData * _Nullable data, NSURLResponse * _Nullable response, NSError * _Nullable connectionError) {
            CDVPluginResult *pluginResult;
            
            if (connectionError == nil) {
                pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK];
            } else {
                // Check for cancelled error codes - TrustKit cancels authentication challenge if pinning is invalid
                if ((connectionError.code == NSURLErrorUserCancelledAuthentication || connectionError.code == NSURLErrorCancelled) && data == nil) {
                    [call reject:@"SSLPinning found a issue with the configured certificate for the url!" :@"1" :nil :nil];
                } else {
                    [call reject:@"SSLPinning found some problem with the request!" :@"2" :nil :nil];
                }
            }
            
            [call resolve];
        }];
        [dataTask resume];
    } else {
        [call reject:@"No Arguments sent!" :@"0" :nil :nil];
    }
}

- (BOOL)handleWKWebViewURLAuthenticationChallenge:(NSURLAuthenticationChallenge* _Nonnull)challenge completionHandler:(void (^_Nonnull)(NSURLSessionAuthChallengeDisposition disposition, NSURLCredential * _Nullable credential))completionHandler
{
    TSKPinningValidator *pinningValidator = [[TrustKit sharedInstance] pinningValidator];
    // Pass the authentication challenge to the TrustKit validator; if the validation fails, the connection will be blocked
    return [pinningValidator handleChallenge:challenge completionHandler:completionHandler];
}

@end
#pragma clang diagnostic pop

