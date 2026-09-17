//
//  OSAppFeedback.m
//  OutSystems
//
//
//

#import "OSAppFeedback.h"
#import "CDVReachability.h"
#import "YoikScreenOrientation.h"

NSString* const kAppFeedbackDefaultHostname = @"DefaultHostname";
NSString* const kAppFeedbackDefaultHandler = @"DefaultAppFeedbackHandler";

@interface OSAppFeedback()

@property (strong, nonatomic) UIView *mobileECTView;
@property (strong, nonatomic) NSString *hostname;
@property (nonatomic) BOOL inBackground;
@property (nonatomic) BOOL hasSettings;
@property (nonatomic) BOOL isECTAvailable;
@property (nonatomic) BOOL defaultHandler;

@end

@implementation OSAppFeedback



-(void)pluginInitialize{
    
    [[NSNotificationCenter defaultCenter] addObserver:self selector:@selector(onPause) name:UIApplicationDidEnterBackgroundNotification object:nil];
    [[NSNotificationCenter defaultCenter] addObserver:self selector:@selector(onResume) name:UIApplicationWillEnterForegroundNotification object:nil];
    
    self.hasSettings = NO;
    self.isECTAvailable = NO;
    
    _mobileECTView = [[UIView alloc] init];
    
    [self.mobileECTView setTranslatesAutoresizingMaskIntoConstraints:NO];
    [self.mobileECTView setBackgroundColor:[UIColor clearColor]];
    
    [self.viewController.view addSubview:_mobileECTView];
    
    [self.viewController.view addConstraint:[NSLayoutConstraint constraintWithItem:self.viewController.view
                                                                         attribute:NSLayoutAttributeCenterX
                                                                         relatedBy:NSLayoutRelationEqual
                                                                            toItem:_mobileECTView
                                                                         attribute:NSLayoutAttributeCenterX
                                                                        multiplier:1
                                                                          constant:0.0]];
    
    [self.viewController.view addConstraint:[NSLayoutConstraint constraintWithItem:self.viewController.view
                                                                         attribute:NSLayoutAttributeCenterY
                                                                         relatedBy:NSLayoutRelationEqual
                                                                            toItem:_mobileECTView
                                                                         attribute:NSLayoutAttributeCenterY
                                                                        multiplier:1
                                                                          constant:0.0]];
    
    [self.viewController.view addConstraint:[NSLayoutConstraint constraintWithItem:self.viewController.view
                                                                         attribute:NSLayoutAttributeHeight
                                                                         relatedBy:NSLayoutRelationEqual
                                                                            toItem:_mobileECTView
                                                                         attribute:NSLayoutAttributeHeight
                                                                        multiplier:1
                                                                          constant:0.0]];
    
    [self.viewController.view addConstraint:[NSLayoutConstraint constraintWithItem:self.viewController.view
                                                                         attribute:NSLayoutAttributeWidth
                                                                         relatedBy:NSLayoutRelationEqual
                                                                            toItem:_mobileECTView
                                                                         attribute:NSLayoutAttributeWidth
                                                                        multiplier:1
                                                                          constant:0.0]];
    
    [self.viewController.view addConstraint:[NSLayoutConstraint constraintWithItem:self.viewController.view
                                                                         attribute:NSLayoutAttributeHeight
                                                                         relatedBy:NSLayoutRelationEqual
                                                                            toItem:_mobileECTView
                                                                         attribute:NSLayoutAttributeHeight
                                                                        multiplier:1
                                                                          constant:0.0]];
    
    [_mobileECTView setHidden:YES];
    
    self.hostname = [[self.commandDelegate settings] objectForKey:[kAppFeedbackDefaultHostname lowercaseString]];
    
    id defaultHandlerSetting = [[self.commandDelegate settings] objectForKey:[kAppFeedbackDefaultHandler lowercaseString]];
    
    if(!defaultHandlerSetting){
        self.defaultHandler = YES;
    }
    else{
        self.defaultHandler = [[[self.commandDelegate settings] objectForKey:kAppFeedbackDefaultHandler] boolValue];
    }
    
    if(self.defaultHandler){
        [self registerGestureHandler];
    }
    
}

-(void) onPause {
    self.inBackground = YES;
}

-(void) onResume {
    self.inBackground = NO;
}


- (AppDelegate *)appDelegate {
    return (AppDelegate*)[[UIApplication sharedApplication] delegate];
}


- (void)deviceready:(CDVInvokedUrlCommand*)command{
    
    if([[command arguments] count] > 0){
        _hostname = [[command arguments] objectAtIndex:0];
    }
    
    [self handleDeviceReady:_hostname];
}

- (void)isAvailable:(CDVInvokedUrlCommand*)command{
    
    if(self.inBackground) {
        return;
    }
    
    self.hasSettings = YES;
    
    [self handleIsECTAvailable:command];
   
}

- (void)openECT:(CDVInvokedUrlCommand*)command{
    
    if(self.inBackground) {
        return;
    }
    
    if(self.hasSettings && self.isECTAvailable) {
        [self.commandDelegate runInBackground:^{
            [self handleOpenECT:command];
        }];
    } else {
        [self.commandDelegate sendPluginResult:[CDVPluginResult resultWithStatus:CDVCommandStatus_OK
                                                                 messageAsString:@"App Feedback is not available."]
                                    callbackId:[command callbackId]];
    }
    
}


-(void)lockToCurrentOrientation {
    
    // Lock orientation
    
    NSString *orientation;
    switch ([[UIDevice currentDevice] orientation]) {
        case UIDeviceOrientationLandscapeLeft:
            orientation = @"landscape-secondary";
            break;
        case UIDeviceOrientationLandscapeRight:
            orientation = @"landscape-primary";
            break;
        case UIDeviceOrientationPortrait:
            orientation = @"portrait-primary";
            break;
        case UIDeviceOrientationPortraitUpsideDown:
            orientation = @"portrait-secondary";
            break;
        default:
            orientation = @"portrait";
    }
    
    CDVViewController* vc = (CDVViewController *) self.viewController;
    
    YoikScreenOrientation* screenOrientationPlugin = (YoikScreenOrientation*)[vc getCommandInstance:@"YoikScreenOrientation"];
    
    NSArray* args = [NSArray arrayWithObjects:@"set", orientation, nil];
    
    CDVInvokedUrlCommand* orientationCommand = [[CDVInvokedUrlCommand alloc] initWithArguments:args
                                                                                    callbackId:@"INVALID"
                                                                                     className:@"YoikScreenOrientation"
                                                                                    methodName:@"screenOrientation"];
    
    
    [screenOrientationPlugin screenOrientation:orientationCommand];
    
}

-(void)unlockOrientation {
    CDVViewController* vc = (CDVViewController *) self.viewController;
    
    YoikScreenOrientation* screenOrientationPlugin = (YoikScreenOrientation*)[vc getCommandInstance:@"YoikScreenOrientation"];
    
    NSArray* args = [NSArray arrayWithObjects:@"set", @"unlocked", nil];
    
    CDVInvokedUrlCommand* orientationCommand = [[CDVInvokedUrlCommand alloc] initWithArguments:args
                                                                                    callbackId:@"INVALID"
                                                                                     className:@"YoikScreenOrientation"
                                                                                    methodName:@"screenOrientation"];
    
    
    [screenOrientationPlugin screenOrientation:orientationCommand];
}

-(void)handleDeviceReady:(NSString*)hostname{
    if(!self.hostname || self.hostname.length == 0){
        self.hostname = hostname;
    }

    [self initEctController:self.hostname];
    
    [self.mobileECTController prepareECTData:^(BOOL result) {
        // do nothing
    }];
}

typedef void(^OSECTAvailabilityBlock)(BOOL);

-(void)checkECTAvailability:(OSECTAvailabilityBlock)completionBlock{
    
    [self.mobileECTController checkECTAvailability:^(BOOL result){
        
        dispatch_sync(dispatch_get_main_queue(), ^{
            [self.mobileECTController prepareForViewWillAppear];
        });
        
        self.isECTAvailable = result;
        
        if(completionBlock)
            completionBlock(result);
    }];
    
}


-(void)handleIsECTAvailable: (CDVInvokedUrlCommand*) command{
    
    if(!self.mobileECTController){
        CDVPluginResult* cdvResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR messageAsString:@"Unable to load App Feedback's native component."] ;
        [self.commandDelegate sendPluginResult:cdvResult callbackId:[command callbackId]];
    }
    
    [self checkECTAvailability:^(BOOL result){
        
        CDVPluginResult* cdvResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK messageAsBool:result] ;
            [self.commandDelegate sendPluginResult:cdvResult callbackId:[command callbackId]];
    }];
    
}


-(void) initEctController:(NSString*) hostname {
    // Mobile ECT Configuration
    self.mobileECTController = [[OSMobileECTController alloc] initWithSuperView:_mobileECTView
                                                                     andWebView:self.webView
                                                                    forHostname:_hostname ];
    
    [self.mobileECTController addOnExitEvent:self withSelector:@selector(onExitECT)];
    
    [self.commandDelegate runInBackground:^{
    
        dispatch_async(dispatch_get_main_queue(), ^{
            [self.mobileECTController prepareForViewDidLoad];
        });
    }];
}

-(void)handleOpenECT:(CDVInvokedUrlCommand*)command {
    
    NSUserDefaults* userDefaults = [NSUserDefaults standardUserDefaults];
    BOOL skipEctHelper = [userDefaults boolForKey:@"OSAPPFEEDBACK_ECT_SKIP_HELPER"];
    
    [self.mobileECTController skipHelper:skipEctHelper];
    
    dispatch_block_t block = ^
    {
        [self lockToCurrentOrientation];
        
        [self.mobileECTController openECTNativeUI];
        [self.mobileECTView setHidden:NO];
    };
    
    if ([NSThread isMainThread])
    {
        block();
    }
    else
    {
        dispatch_sync(dispatch_get_main_queue(), block);
    }
    
    if(command){
        [self.commandDelegate sendPluginResult:[CDVPluginResult resultWithStatus:CDVCommandStatus_OK] callbackId:[command callbackId]];
    }
}

-(void)onExitECT{
    dispatch_async(dispatch_get_main_queue(), ^{
        [self unlockOrientation];
        [self.mobileECTView setHidden:YES];
        NSUserDefaults* userDefaults = [NSUserDefaults standardUserDefaults];
        [userDefaults setBool:YES forKey:@"OSAPPFEEDBACK_ECT_SKIP_HELPER"];
    });
}

# pragma mark - Open gesture handling
-(void)registerGestureHandler{

    UILongPressGestureRecognizer* longPressRecognizer = [[UILongPressGestureRecognizer alloc] initWithTarget:self action:@selector(handleOpenGesture:)];
    [longPressRecognizer setMinimumPressDuration:0.6];
    [longPressRecognizer setNumberOfTouchesRequired:2];
    
    [self.webView addGestureRecognizer:longPressRecognizer];
    
}

-(void)handleOpenGesture:(UILongPressGestureRecognizer*) gestureRecognizer {
    if([gestureRecognizer state] == UIGestureRecognizerStateBegan) {
        
        if ([[CDVReachability reachabilityForInternetConnection]currentReachabilityStatus] != NotReachable){
            [self checkECTAvailability:^(BOOL result){
                if(result){
                    dispatch_async(dispatch_get_main_queue(), ^{
                        [self handleOpenECT:nil];
                    });
                }
            }];
        }
        else{
            UIAlertController *alert = [UIAlertController alertControllerWithTitle:@"Can't send feedback"
                                                                           message:@"Make sure your device has internet connection and try again."
                                                                    preferredStyle:UIAlertControllerStyleAlert];
            
            [alert addAction:[UIAlertAction actionWithTitle:@"OK"
                                                      style:UIAlertActionStyleDefault
                                                    handler:nil]];
            
            [self.viewController presentViewController:alert animated:YES completion:nil];
        }
    }
}

@end
