#import "CDVErrorScreen.h"
#import "ConnectionFailedViewController.h"

#define DEFAULT_COLOR @"#C20000"
#define HEX_REGEX @"^#([A-Fa-f0-9]{6})$"

@implementation CDVErrorScreen

- (void)pluginInitialize {
    NSDictionary* settings = [self.commandDelegate settings];

    _connectionFailedVC = [[ConnectionFailedViewController alloc] init];
    backgroundColor = [settings objectForKey:@"backgroundcolor"];

    NSError* error = nil;
    NSRegularExpression* regex = [NSRegularExpression regularExpressionWithPattern:HEX_REGEX options:NSRegularExpressionCaseInsensitive error:&error];

    if(backgroundColor == nil || [regex numberOfMatchesInString:backgroundColor options:0 range:NSMakeRange(0, [backgroundColor length])] == 0){
        backgroundColor = DEFAULT_COLOR;
    }
}

- (void)show:(CDVInvokedUrlCommand *)command {
    [self.viewController addChildViewController:_connectionFailedVC];
    [self.viewController willMoveToParentViewController:_connectionFailedVC];

    _connectionFailedVC.view.frame = [self.viewController.view bounds];
    _connectionFailedVC.view.backgroundColor = [CDVErrorScreen colorFromHexString:backgroundColor];
    _connectionFailedVC.delegate = self;

    [self.viewController.view addSubview:_connectionFailedVC.view];
}

- (void)hide:(CDVInvokedUrlCommand *)command {
    [_connectionFailedVC removeFromParentViewController];
    [_connectionFailedVC.view removeFromSuperview];
}

- (void)onConnectionFailedTryAgainItemTouched{
    [self.webViewEngine evaluateJavaScript:@"window.location.reload(true)" completionHandler:^(id result, NSError *error) {}];
}

+ (UIColor *)colorFromHexString:(NSString *)hexString {
    unsigned rgbValue = 0;
    NSScanner *scanner = [NSScanner scannerWithString:hexString];
    [scanner setScanLocation:1]; // bypass '#' character
    [scanner scanHexInt:&rgbValue];
    return [UIColor colorWithRed:((rgbValue & 0xFF0000) >> 16)/255.0 green:((rgbValue & 0xFF00) >> 8)/255.0 blue:(rgbValue & 0xFF)/255.0 alpha:1.0];
}

@end
