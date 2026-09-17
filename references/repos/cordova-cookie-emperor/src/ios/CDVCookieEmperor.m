#import "CDVCookieEmperor.h"

@implementation CDVCookieEmperor

 - (void)getCookieValue:(CDVInvokedUrlCommand*)command
{
    CDVPluginResult* pluginResult = nil;

    NSString* urlString = [command.arguments objectAtIndex:0];
    __block NSString* cookieName = [command.arguments objectAtIndex:1];

    if (urlString != nil)
    {
        NSArray* cookies = [[NSHTTPCookieStorage sharedHTTPCookieStorage] cookiesForURL:[NSURL URLWithString:urlString]];
        __block NSString *cookieValue;

        [cookies enumerateObjectsUsingBlock:^(id obj, NSUInteger idx, BOOL *stop) {
            NSHTTPCookie *cookie = obj;

            if([cookie.name isEqualToString:cookieName])
            {
                cookieValue = cookie.value;
                *stop = YES;
            }
        }];

        if (cookieValue != nil)
        {
            pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK messageAsDictionary:@{@"cookieValue":cookieValue}];
        }
        else
        {
            pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR messageAsString:@"No cookie found"];
        }

    }
    else
    {
        pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR messageAsString:@"URL was null"];
    }

    [self.commandDelegate sendPluginResult:pluginResult callbackId:command.callbackId];
}

 - (void)setCookieValue:(CDVInvokedUrlCommand*)command
{
    [[NSHTTPCookieStorage sharedHTTPCookieStorage] setCookieAcceptPolicy:NSHTTPCookieAcceptPolicyAlways];

    CDVPluginResult* pluginResult = nil;
    NSString* urlString = [command.arguments objectAtIndex:0];
    NSString* cookieName = [command.arguments objectAtIndex:1];
    NSString* cookieValue = [command.arguments objectAtIndex:2];
    
    NSMutableDictionary *cookieProperties = [NSMutableDictionary dictionary];
    
    // Parse cookie properties from "cookieValue" value
    NSArray *cookieKeyValue = [cookieValue componentsSeparatedByString:@";"];
    for (NSString *property in cookieKeyValue) {
        NSRange separator = [property rangeOfString:@"="];
        if(separator.location != NSNotFound && separator.location > 0
           && separator.location <= ([property length] -1 )) {
            
            NSRange keyRange = NSMakeRange(0, separator.location);
            NSString* key = [property substringWithRange:keyRange];
            NSString* value= [property substringFromIndex:(separator.location + separator.length)];
            
            key = [key stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceAndNewlineCharacterSet]];
            value = [value stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceAndNewlineCharacterSet]];
            NSString *uppercaseKey = [key uppercaseString];
            if ([uppercaseKey isEqualToString:@"VERSION"]) {
                [cookieProperties setObject:value forKey:NSHTTPCookieVersion];
            } else if ([uppercaseKey isEqualToString:@"MAX-AGE"]||[uppercaseKey isEqualToString:@"MAXAGE"]) {
                [cookieProperties setObject:value forKey:NSHTTPCookieMaximumAge];
            } else if ([uppercaseKey isEqualToString:@"PATH"]) {
                [cookieProperties setObject:value forKey:NSHTTPCookiePath];
            } else if([uppercaseKey isEqualToString:@"PORT"]){
                [cookieProperties setObject:value forKey:NSHTTPCookiePort];
            } else if([uppercaseKey isEqualToString:@"SECURE"]||[uppercaseKey isEqualToString:@"ISSECURE"]){
                [cookieProperties setObject:value forKey:NSHTTPCookieSecure];
            } else if([uppercaseKey isEqualToString:@"COMMENT"]){
                [cookieProperties setObject:value forKey:NSHTTPCookieComment];
            } else if([uppercaseKey isEqualToString:@"COMMENTURL"]){
                [cookieProperties setObject:value forKey:NSHTTPCookieCommentURL];
            } else if([uppercaseKey isEqualToString:@"EXPIRES"]){
                NSDateFormatter *dateFormatter = [[NSDateFormatter alloc] init];
                [dateFormatter setLocale:[[NSLocale alloc] initWithLocaleIdentifier:@"en_US"]];
                dateFormatter.dateFormat = @"EEE, dd-MMM-yyyy HH:mm:ss zzz";
                [cookieProperties setObject:[dateFormatter dateFromString:value] forKey:NSHTTPCookieExpires];
            } else if([uppercaseKey isEqualToString:@"DISCARD"]){
                [cookieProperties setObject:value forKey:NSHTTPCookieDiscard];
            } else if([uppercaseKey isEqualToString:@"NAME"]){
                [cookieProperties setObject:value forKey:NSHTTPCookieName];
            } else if([uppercaseKey isEqualToString:@"VALUE"]){
                [cookieProperties setObject:value forKey:NSHTTPCookieValue];
            } else if([uppercaseKey isEqualToString:@"DOMAIN"]) {
                [cookieProperties setObject:value forKey:NSHTTPCookieDomain];
            }
        }
    }
    
    
    if (![cookieProperties objectForKey:NSHTTPCookiePath]) {
        [cookieProperties setObject:@"/" forKey:NSHTTPCookiePath];
    }
    
    // Best effort to retrieve the cookie value from a string with the following type:
    // someCookieValue; path=/; expires= ....; ...
    if (![cookieProperties objectForKey:NSHTTPCookieValue]) {
        if([cookieKeyValue count] > 0) {
            NSString* value = [cookieKeyValue firstObject];
            if([value rangeOfString:@"="].location == NSNotFound) {
                [cookieProperties setObject:value forKey:NSHTTPCookieValue];
            }
        }
    }

    [cookieProperties setObject:cookieName forKey:NSHTTPCookieName];
    [cookieProperties setObject:urlString forKey:NSHTTPCookieOriginURL];
    
    NSHTTPCookie *cookie = [NSHTTPCookie cookieWithProperties:cookieProperties];
    [[NSHTTPCookieStorage sharedHTTPCookieStorage] setCookie:cookie];

    NSArray* cookies = [NSArray arrayWithObjects:cookie, nil];

    NSURL *url = [[NSURL alloc] initWithString:urlString];
    [[NSHTTPCookieStorage sharedHTTPCookieStorage] setCookies:cookies forURL:url mainDocumentURL:nil];

    pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK messageAsString:@"Set cookie executed"];
    [self.commandDelegate sendPluginResult:pluginResult callbackId:command.callbackId];
}

- (void)clearCookies:(CDVInvokedUrlCommand*)command
{
    NSHTTPCookie *cookie;
    NSHTTPCookieStorage *storage = [NSHTTPCookieStorage sharedHTTPCookieStorage];

    for (cookie in [storage cookies])
    {
        [storage deleteCookie:cookie];
    }

    [[NSUserDefaults standardUserDefaults] synchronize];

    CDVPluginResult* pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK];
    [self.commandDelegate sendPluginResult:pluginResult callbackId:command.callbackId];
}

@end
