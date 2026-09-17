//
//  MobileECTPlugin.m
//  OutSystems - Mobility Experts
//
//  Created by Vitor Oliveira on 14/010/15.
//
//

#import "BlinkIdPlugin.h"

@implementation BlinkIdPlugin

- (void) readCardId:(CDVInvokedUrlCommand*)command {
    self.commandHelper = command;
    CDVPluginResult* pluginResult;
    
    /** Read the License Key */
    NSString* licenseKey;
    
    if([command.arguments count] && [command.arguments count] > 0)
        licenseKey = [command.arguments objectAtIndex:0];
        
    /** Check there is a license key */
    if (!licenseKey) {
        pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR messageAsString:@"Is mandatory a license key to use the this plugin"];
        [self.commandDelegate sendPluginResult:pluginResult callbackId:command.callbackId];
        return;
    }
    
    /** Set the license key to use when the BlinkID framework was launched */
    self.mLicenseKey = licenseKey;
    
    /** Instantiate the scanning coordinator */
    NSError *error;
    PPCoordinator *coordinator = [self coordinatorWithError:&error];
    
    /** If scanning isn't supported, present an error */
    if (coordinator == nil) {
        NSString *messageString = [error localizedDescription];
        [[[UIAlertView alloc] initWithTitle:@"Warning"
                                    message:messageString
                                   delegate:nil
                          cancelButtonTitle:@"OK"
                          otherButtonTitles:nil, nil] show];
        pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR messageAsString:messageString];
        [self.commandDelegate sendPluginResult:pluginResult callbackId:command.callbackId];
        return;
    }
    
    /** Allocate and present the scanning view controller */
    UIViewController<PPScanningViewController>* scanningViewController = [coordinator cameraViewControllerWithDelegate:self];
    
    /** You can use other presentation methods as well */
    [[self currentApplicationViewController] presentViewController:scanningViewController animated:YES completion:nil];
}

- (void) scannDocument:(CDVInvokedUrlCommand*)command {
    /** TO BE DEVELOPMENT */
}

/**
 * Method to Get the Current Application View Controller
 *
 * @return the Application View Controller
 */
- (UIViewController *) currentApplicationViewController {
    UIWindow *window = [UIApplication sharedApplication].keyWindow;
    UIViewController *rootViewController = window.rootViewController;
    
    if([rootViewController isKindOfClass:[UIViewController class]]){
        return [[UIApplication sharedApplication]delegate].window.rootViewController;
    } else {
        UINavigationController *navigationController = (UINavigationController *)[[UIApplication sharedApplication]delegate].window.rootViewController;
        return(UIViewController *)[navigationController topViewController];
    }
    return nil;
}


#pragma mark - BlinkId
/**
 * Method allocates and initializes the Scanning coordinator object.
 * Coordinator is initialized with settings for scanning
 *
 *  @param error Error object, if scanning isn't supported
 *
 *  @return initialized coordinator
 */
- (PPCoordinator *)coordinatorWithError:(NSError**)error {
    
    /** 0. Check if scanning is supported */
    
    if ([PPCoordinator isScanningUnsupported:error]) {
        return nil;
    }
    
    
    /** 1. Initialize the Scanning settings */
    
    // Initialize the scanner settings object. This initialize settings with all default values.
    PPSettings *settings = [[PPSettings alloc] init];
    
    
    /** 2. Setup the license key */
    
    // Visit www.microblink.com to get the license key for your app
    settings.licenseSettings.licenseKey = self.mLicenseKey;
    
    /**
     * 3. Set up what is being scanned. See detailed guides for specific use cases.
     * Here's an example for initializing MRTD and USDL scanning
     */
    
    // To specify we want to perform MRTD (machine readable travel document) recognition, initialize the MRTD recognizer settings
    PPMrtdRecognizerSettings *mrtdRecognizerSettings = [[PPMrtdRecognizerSettings alloc] init];
    
    // Add MRTD Recognizer setting to a list of used recognizer settings
    [settings.scanSettings addRecognizerSettings:mrtdRecognizerSettings];
    
    // To specify we want to perform USDL (US Driver's license) recognition, initialize the USDL recognizer settings
    PPUsdlRecognizerSettings *usdlRecognizerSettings = [[PPUsdlRecognizerSettings alloc] init];
    
    // Add USDL Recognizer setting to a list of used recognizer settings
    [settings.scanSettings addRecognizerSettings:usdlRecognizerSettings];
    
    
    /** 4. Initialize the Scanning Coordinator object */
    
    PPCoordinator *coordinator = [[PPCoordinator alloc] initWithSettings:settings];
    
    return coordinator;
}

- (void)scanningViewControllerUnauthorizedCamera:(UIViewController<PPScanningViewController> *)scanningViewController {
    // Add any logic which handles UI when app user doesn't allow usage of the phone's camera
    __block CDVPluginResult* pluginResult = nil;
    pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR messageAsString:@"Not Allowed to Use the Camera"];
    
    [self.commandDelegate sendPluginResult:pluginResult callbackId:self.commandHelper.callbackId];
}

- (void)scanningViewController:(UIViewController<PPScanningViewController> *)scanningViewController
                  didFindError:(NSError *)error {
    // Can be ignored. See description of the method
    __block CDVPluginResult* pluginResult = nil;
    pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR messageAsString:@"Error doing Blink ID Scanner"];
    
    [self.commandDelegate sendPluginResult:pluginResult callbackId:self.commandHelper.callbackId];
}

- (void)scanningViewControllerDidClose:(UIViewController<PPScanningViewController> *)scanningViewController {
    
    // As scanning view controller is presented full screen and modally, dismiss it
    [[self currentApplicationViewController] dismissViewControllerAnimated:YES completion:nil];
    
    __block CDVPluginResult* pluginResult = nil;
    pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR messageAsString:@"Close Blink ID Scanner"];

    [self.commandDelegate sendPluginResult:pluginResult callbackId:self.commandHelper.callbackId];

}

- (void)scanningViewController:(UIViewController<PPScanningViewController> *)scanningViewController
              didOutputResults:(NSArray *)results {
    
    // Here you process scanning results. Scanning results are given in the array of PPRecognizerResult objects.
    
    // first, pause scanning until we process all the results
    [scanningViewController pauseScanning];
    
    NSString* message;
    NSString* title;
    
    // Collect data from the result
    for (PPRecognizerResult* result in results) {
        
        if ([result isKindOfClass:[PPMrtdRecognizerResult class]]) {
            PPMrtdRecognizerResult* mrtdResult = (PPMrtdRecognizerResult*)result;
            title = @"MRTD";
            message = [mrtdResult description];
            
            NSDictionary *jsonObj = [ [NSDictionary alloc]
                                     initWithObjectsAndKeys :
                                     [mrtdResult isParsed] ? @"true" : @"false", @"isParsed",
                                     [mrtdResult issuer], @"issuer",
                                     [mrtdResult documentNumber], @"documentNumber",
                                     [mrtdResult documentCode], @"documentCode",
                                     [mrtdResult dateOfExpiry], @"dateOfExpiry",
                                     [mrtdResult primaryId], @"primaryId",
                                     [mrtdResult secondaryId], @"secondaryId",
                                     [mrtdResult dateOfBirth], @"dateOfBirth",
                                     [mrtdResult nationality], @"nationality",
                                     [mrtdResult sex], @"sex",
                                     [mrtdResult opt1], @"opt1",
                                     [mrtdResult opt2], @"opt2",
                                     [mrtdResult mrzText], @"mrzText",
                                     nil
                                     ];
            
            NSError *error;
            NSData *jsonData = [NSJSONSerialization dataWithJSONObject:jsonObj
                                                               options:NSJSONWritingPrettyPrinted // Pass 0 if you don't care about the readability of the generated string
                                                                 error:&error];
            NSString *jsonString = @"";
            if (! jsonData) {
                NSLog(@"Got an error: %@", error);
            } else {
                jsonString = [[NSString alloc] initWithData:jsonData encoding:NSUTF8StringEncoding];
            }
            
            
            __block CDVPluginResult* pluginResult = nil;
            //pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK messageAsDictionary:jsonObj];
            pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK messageAsString:jsonString];
            
            [self.commandDelegate sendPluginResult:pluginResult callbackId:self.commandHelper.callbackId];
            
        }
        if ([result isKindOfClass:[PPUsdlRecognizerResult class]]) {
            PPUsdlRecognizerResult* usdlResult = (PPUsdlRecognizerResult*)result;
            title = @"USDL";
            message = [usdlResult description];

            NSDictionary *jsonObj = [[NSDictionary alloc]
                                      initWithObjectsAndKeys:
                                        [usdlResult getField:kPPIssuerIdentificationNumber], @"issuer",
                                        [usdlResult getField:kPPCountryIdentification], @"nationality",
                                        [usdlResult getField:kPPCustomerIdNumber], @"documentNumber",
                                        [usdlResult getField:kPPCustomerFamilyName], @"customerFamilyName",
                                        [usdlResult getField:kPPCustomerFirstName], @"customerFirstName",
                                        [usdlResult getField:kPPDateOfBirth], @"dateOfBirth",
                                        [usdlResult getField:kPPEyeColor], @"eyeColor",
                                        [usdlResult getField:kPPHeight], @"height",
                                        [usdlResult getField:kPPAddressStreet], @"addressStreet",
                                        [usdlResult getField:kPPAddressCity], @"addressCity",
                                        [usdlResult getField:kPPAddressJurisdictionCode], @"addressJurisdictionCode",
                                        [usdlResult getField:kPPAddressPostalCode], @"addressPostalCode",
                                        [usdlResult getField:kPPDocumentIssueDate], @"documentIssueDate",
                                        [usdlResult getField:kPPJurisdictionVersionNumber], @"jurisdictionVersionNumber",
                                        [usdlResult getField:kPPJurisdictionVehicleClass], @"jurisdictionVehicleClass",
                                        [usdlResult getField:kPPCustomerFullName], @"customerFullName",
                                        [usdlResult getField:kPPDocumentExpirationDate], @"dateOfExpiry",
                                        [usdlResult getField:kPPSex], @"sex",
                                        @"true", @"isLicenseDriver",
                                        nil
                                        ];

            NSError* error ;
            NSData *jsonData = [NSJSONSerialization dataWithJSONObject:jsonObj
                                                               options:NSJSONWritingPrettyPrinted
                                                                 error:&error];
            NSString *jsonString = @"";
            if (! jsonData) {
                NSLog(@"Got an error: %@", error);
            } else {
                jsonString = [[NSString alloc] initWithData:jsonData encoding:NSUTF8StringEncoding];
            }
        
            __block CDVPluginResult* pluginResult = nil;
            pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK messageAsString:jsonString];
            
            [self.commandDelegate sendPluginResult:pluginResult callbackId:self.commandHelper.callbackId];
            
        }
    };
    
    // present the alert view with scanned results
   // UIAlertView *alertView = [[UIAlertView alloc] initWithTitle:title message:message delegate:self cancelButtonTitle:@"OK" otherButtonTitles:nil];
    //[alertView show];
    [[self currentApplicationViewController] dismissViewControllerAnimated:YES completion:nil];
}

// dismiss the scanning view controller when user presses OK.
- (void)alertView:(UIAlertView *)alertView clickedButtonAtIndex:(NSInteger)buttonIndex {
    [[self currentApplicationViewController] dismissViewControllerAnimated:YES completion:nil];
}


@end
