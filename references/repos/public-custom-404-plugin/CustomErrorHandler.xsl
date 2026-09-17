<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">

    <xsl:output method="xml" indent="yes" encoding="UTF-8"/> 

    <!-- Define new Error page redirect string -->
    <xsl:param name="new404" select="'/Custom404Plugin/NotFound.aspx?app=MyStore&amp;page=NotFound'"/>

    <xsl:template match="@*|node()">
        <xsl:copy>
            <xsl:apply-templates select="@*|node()"/>
        </xsl:copy>
    </xsl:template>
        
    <xsl:template match="/configuration/system.webServer">
        <xsl:copy>
            <xsl:apply-templates select="@*|node()"/>
            <httpErrors>
                <!-- Define new Error page -->
                <error statusCode="404" path="/Custom404Plugin/NotFound.aspx?app=MyStore&amp;page=NotFound" subStatusCode="0" responseMode="ExecuteURL"/>
            </httpErrors>
        </xsl:copy>
    </xsl:template>

    <!-- Change 404 redirect -->
    <xsl:template match="/configuration/system.web/customErrors/error/@redirect">
        <xsl:attribute name="redirect">
            <xsl:value-of select="$new404"/>
        </xsl:attribute>
    </xsl:template>

</xsl:stylesheet>
