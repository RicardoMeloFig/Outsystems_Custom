# Configuring custom 404 error screens
Public Documentation

## Introduction
The OutSystems Platform handles unforeseen or unhandled errors that might occur in applications and displays feedback pages to end-users. You can create your own custom HTTP 404 (Not Found) error pages in Service Studio and configure them for your reactive applications.
This applies to both on-premise and cloud customers. The method described in the Customizing OutSystems Platform Pages for Unhandled Errors article is still valid for on-premise customers, although more prone to errors.

## Create a custom error page
This will be the page that will be shown in your application when the user tries to access a page that does not exist. It is recommended to have your error page inside the application it was created for. In this case, our demo application is called MyStore and we will create an error page inside it.
1. Add a new screen to that application. For this example, we named it “NotFound”, but you can call it as you want.
2. Tweak it as you like.
3. Publish it.

When modifying your custom error page, you can add buttons and links to other pages or screens in the same application or domain. However, if you need to add a link to an external web page, you will need to specify the attribute `target` with the value `_top` to prevent unexpected behaviors.

## Install the Forge plugin called “Custom 404 Plugin”
1. Go to Forge and search for: Custom 404 Plugin.
2. Install it in your factory.
3. Reference it in your application.
4. In the error page you create, edit the OnDestroy event and add a call to the action NotFoundOnDestroy. This step is very important as it cleans some internal configurations before navigating out of the NotFound page.


## Create a shared configuration
Now that you have an application that contains your custom error page, you need to create some redirect rules that make the OutSystems platform use it as the default error page of your applications. You will add these redirect rules to a shared configuration file and then associate it to your application. You can create as many shared configurations as needed.

For that you need to go to Forge and install the module “Factory Configuration” into your factory.
1. In the Factory Configuration app, go to Shared Configurations.
2. Create a new Shared Configuration.
3. In Pre-made Samples select “Custom Handler (.NET)”.
4. Click on the “Fill” button.
5. Give it a name.

Now you will have to modify its content so that it points to the custom 404 error page you just created. In our example, that screen is called “NotFound” and it is inside the same application we are tweaking, which is called “MyStore”. Please replace MyStore and NotFound by the name of your application and error page respectively.

Modify the value field, in the “httpErrors” section. It has to contain these three snippets:
First one:
```xslt
<!-- Define new Error page redirect string -->
<xsl:param name="new404" select="'..'/Custom404Plugin/NotFound.aspx?app=MyStore&amp;page=NotFound'"/>
```
Second one:
```xslt
<error statusCode="404" path="/Custom404Plugin/NotFound.aspx?app=MyStore&amp;page=NotFound" subStatusCode="0" responseMode="ExecuteURL"/>
```
Third one:
```xslt
<!-- Change 404 redirect -->
<xsl:template match="/configuration/system.web/customErrors/error/@redirect">
    <xsl:attribute name="redirect">
        <xsl:value-of select="$new404"/>
    </xsl:attribute>
</xsl:template>
```
Click on “Save”.

Check [this file](https://github.com/dawud-outsystems/public-custom-404-plugin/blob/main/CustomErrorHandler.xsl) for an example configuration.

You can have as many configurations as you want. Usually it will be one per application.

## Apply the shared configuration to your apps
Now that your configuration is ready, you need to specify which applications it will apply to.
1. In Factory Configuration, go to eSpaces.
2. Search for the application you want to apply the configuration to.
3. Select it in the list.
4. Choose your shared configuration in the dropdown menu.
5. Click on the “Associate Shared Configuration” button.
6. Repeat the process for all the applications you want to add a custom error page to.
7. Republish all the modified applications in Service Studio.
If you want to define a custom error page for another application, you can follow the same steps and apply the configuration only for that application.

## Optional: Configure a site rule
It might happen for a user to mistype the name of a specific application, for example typing `https://mysite.something/MyStoree` instead of `https://mysite.something/MyStore`. In that case, the error page associated with MyStore won’t be opened, as the user failed to access that specific module.

In order to address that situation, you need to define which application will be automatically opened when accessing your domain, and create a custom error page for it. This can be a blank application, or a real application with functionality. You can do that by configuring a site rule:
1. Go to Service Center / Administration / SEO URLs / Site Rules.
2. Click on New Site Rule.
3. Write the domain of your OutSystems’ Platform in “Base URL”.
4. Select your application in “Root Application”.
5. Click on “Create”.
6. Configure a custom 404 error page for it by following the steps explained before.
7. Official documentation can be found at [this location](https://success.outsystems.com/Documentation/11/Developing_an_Application/SEO_in_Reactive_Web_Apps).

## Troubleshooting common errors
Please note that modifying the XML is an error-prone operation. In case the XML modified in the previous step is invalid, Service Studio will show a warning message when republishing the modified application, saying that the config was invalid and will be disregarded. In that case, please review your configuration file.

Look for the following common mistakes:
### Not including single quotes:
```xslt
<!-- Define new Error page redirect string -->
<xsl:param name="new404" select="'/Custom404Plugin/NotFound.aspx?app=MyStore&amp;page=NotFound'"/>
```
Make sure the URL defined for the variable “new404” is between single quotes and double quotes simultaneously.

### Mistyping URLs:
```xslt
<error statusCode="404" path="/Custom404Plugin/NotFound.aspx?app=MyStore&amp;page=NotFound" subStatusCode="0" responseMode="ExecuteURL"/>
```
Make sure to correctly type the URL, including:
- your application name,
- your not-found page name,
- forward slashes,
- question mark,
- and escaping the ampersand
