const axios = require('axios');

module.exports = {
    getJsonFile: async function getJsonFile(endpoint, appName){
        try {
            let response = await axios.get(endpoint, {
                params: { AppName : appName }
            });
        
            let json = response.data; 
            return json;
        } catch(err){
            if(err.response){
                if(err.response.status == 400){
                    let data = err.response.data;
                    if(data.Errors){
                        console.error(data.Errors);
                        throw new Error(`OUTSYSTEMS_PLUGIN_ERROR: Something went wrong with the request. Check the logs for more information.`);
                    } else throw new Error("OUTSYSTEMS_PLUGIN_ERROR: Bad Request - make sure your app and SOCIAL_CONF_API_ENDPOINT are configured correctly.")
                }
                if(err.response.status == 404){
                    throw new Error("OUTSYSTEMS_PLUGIN_ERROR: Not found - Social Logins Configurator is either outdated or SOCIAL_CONF_API_ENDPOINT is not well defined.");
                }
            } else if(err.request){
                console.error(err.toJSON());
                throw new Error(`OUTSYSTEMS_PLUGIN_ERROR: Something went wrong with the request. Check the logs for more information.`);
            } else{
                throw new Error(`OUTSYSTEMS_PLUGIN_ERROR: Unexpected error - ${err.message}`)
            }
        }
    },
    ProvidersEnum: Object.freeze({"apple":"1", "facebook":"2", "google":"3", "linkedIn":"4"}),
    ApplicationTypeEnum: Object.freeze({"web":"1", "ios":"2", "android":"3"})
}
