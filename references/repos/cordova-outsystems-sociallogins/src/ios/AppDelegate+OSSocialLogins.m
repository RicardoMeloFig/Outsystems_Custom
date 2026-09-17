#import "AppDelegate+OSSocialLogins.h"

#import <OSSocialLoginsLib/OSSocialLoginsLib-Swift.h>
#import <objc/runtime.h>

@implementation AppDelegate (OSSocialLogins)

+ (void)load {
    Method original = class_getInstanceMethod(self, @selector(application:didFinishLaunchingWithOptions:));
    Method swizzled = class_getInstanceMethod(self, @selector(application:socialLoginsPluginDidFinishLaunchingWithOptions:));
    method_exchangeImplementations(original, swizzled);
}

- (BOOL)application:(UIApplication *)application socialLoginsPluginDidFinishLaunchingWithOptions:(NSDictionary *)launchOptions {
    [self application:application socialLoginsPluginDidFinishLaunchingWithOptions:launchOptions];
    
    (void)[SocialLoginsApplicationDelegate.shared application:application didFinishLaunchingWithOptions:launchOptions];
    
    return YES;
}

- (BOOL)application:(UIApplication *)app openURL:(NSURL *)url options:(NSDictionary<UIApplicationOpenURLOptionsKey,id> *)options {
    // gets the passed url's `urlScheme`
    NSString *currentUrlScheme = [url.absoluteString componentsSeparatedByString:@"://"].firstObject;
    
    // check if it should delegate to the library or to `CDVAppDelegate`
    return [self shouldDelegateToLibrary:currentUrlScheme] ? [SocialLoginsApplicationDelegate.shared application:app openURL:url options:options] : [super application:app openURL:url options:options];
}

- (BOOL)shouldDelegateToLibrary:(NSString *)openURLScheme {
    // fetch all app's `utlTypes`
    NSArray *urlTypes = [NSBundle.mainBundle objectForInfoDictionaryKey:@"CFBundleURLTypes"];
    NSPredicate *predicate = [NSPredicate predicateWithBlock:^BOOL(id  evaluatedObject, NSDictionary<NSString *,id> * bindings) {
        // fetch the `urlSchemes` associated to the evaluated `urlType`
        NSArray * urlSchemeArray = evaluatedObject[@"CFBundleURLSchemes"];
        // verifies if the `urlType` relates with the `urlScheme` of the to-be-opened URL
        return [urlSchemeArray containsObject:openURLScheme];
    }];
    
    // applying the filter, it fetches the `urlName` of the matching `urlType`
    NSString *urlName = [urlTypes filteredArrayUsingPredicate:predicate].firstObject[@"CFBundleURLName"];
    // checks if the matching `urlName` is one that can be delegated to the library.
    return [@[@"Google", @"Facebook", @"DeepLinkScheme"] containsObject:urlName];
}

@end
