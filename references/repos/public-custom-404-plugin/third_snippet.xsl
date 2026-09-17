<!-- Change 404 redirect -->
<xsl:template match="/configuration/system.web/customErrors/error/@redirect">
    <xsl:attribute name="redirect">
        <xsl:value-of select="$new404"/>
    </xsl:attribute>
</xsl:template>
