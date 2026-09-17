//
//  LogOut.h
//  OutSystems
//
//  Created by Vitor Oliveira on 26/11/14.
//  @author Vítor Oliveira (vitor.oliveira@outsystems.com)
//  @author João Pereira Gonçalves (joao.goncalves@outsystems.com)
//

#import "LogOut.h"
#import "ApplicationViewController.h"
#import "OSNavigationController.h"

@implementation LogOut

- (OSNavigationController *) navigationController {
    if([self.appDelegate.window.rootViewController class] == [OSNavigationController class]){
        return (OSNavigationController*) self.appDelegate.window.rootViewController;
    }
    return nil;
}

- (void)logOut:(CDVInvokedUrlCommand *)command {
    CDVPluginResult* pluginResult;
    if([self navigationController]) {
        pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK];
        [[self navigationController] popViewControllerAnimated:YES];
    } else {
        pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR];
    }
    
    [self.commandDelegate sendPluginResult:pluginResult callbackId:command.callbackId];
}

- (void)logOutWebView:(CDVInvokedUrlCommand *)command {
    CDVPluginResult* pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK];
    UIAlertView *alert = [[UIAlertView alloc] initWithTitle:@"Sign Off Verification"
                                                    message:@"You're about to sign off. Are you sure?"
                                                   delegate:self
                                          cancelButtonTitle:@"Cancel"
                          
                                          otherButtonTitles:@"Yes", nil];
    [alert show];
    
    [self.commandDelegate sendPluginResult:pluginResult callbackId:command.callbackId];
}

- (OutSystemsAppDelegate *)appDelegate {
    return (OutSystemsAppDelegate*)[[UIApplication sharedApplication] delegate];
}

- (void)alertView:(UIAlertView *)alertView clickedButtonAtIndex:
(NSInteger)buttonIndex {
    if (buttonIndex == 1) {
        // do code for yes click
        
        NSHTTPCookieStorage *storage = [NSHTTPCookieStorage sharedHTTPCookieStorage];
        for (NSHTTPCookie *cookie in [storage cookies]) {
            [storage deleteCookie:cookie];
        }
        [[NSUserDefaults standardUserDefaults] synchronize];
        
        NSURL * url = self.webView.request.URL;
        [self.webView loadRequest:[NSURLRequest requestWithURL:[NSURL URLWithString:[url absoluteString]]]];
    } else {
        // otherwise
    }
}
@end