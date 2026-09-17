use std::fs::File;
use std::time::Instant;
use std::path::Path;
use std::io::prelude::*;
use handlebars::{Handlebars};
use anyhow::{Context, Result};
use log::{debug, info};

use crate::parameters::{Parameters, verify_file_exists};
use crate::statistics::Statistics;


pub fn generate_html(statistics: &Statistics, param: &Parameters) -> Result<()> {
    let start = Instant::now();
    info!("Generating HTML..");
    let template_location = verify_file_exists(&"templates/template.hbs".to_string());

    let mut handlebars = Handlebars::new();
    handlebars.register_template_file("table", &template_location)
        .with_context(|| format!("Could not register handlebars template {:?}", &template_location))?;

    let base_path = Path::new(&param.target_directory);
    let index_path = base_path.join("index.htm");
    let index_content = handlebars.render("table", &statistics.jsonify()).unwrap();
    let mut index_file = File::create(&index_path)
        .with_context(|| format!("Could not create file {:?}", &index_path))?;
    index_file.write_all(index_content.as_bytes())
        .with_context(|| format!("Could not write to file {:?}", index_path))?;

    debug!("Sequencial HTML generation: {:?}", start.elapsed());
    Ok(())
}