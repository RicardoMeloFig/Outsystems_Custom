mod csv_extracter;
mod data_loader;
mod html_renderer;
mod parameters;
mod plotter;
mod statistics;

use std::time::Instant;
use std::path::PathBuf;
use anyhow::Result;
use log::info;

use crate::csv_extracter::extract_columns_base;
use crate::data_loader::LoadedData;
use crate::html_renderer::generate_html;
use crate::parameters::Parameters;
use crate::plotter::generate_plots;
use crate::statistics::Statistics;


fn main() -> Result<()>  {
    let start = Instant::now();
    let (param, file_list) = Parameters::obtain();
    param.print();
    let parsed_files_list: Vec<PathBuf> = file_list.clone().into_iter().map(|path| path.with_extension("_altered.csv")).collect();

    extract_columns_base(&file_list, &parsed_files_list, &param);
    let loaded_data = LoadedData::load_file_data(&parsed_files_list, &param).unwrap();
    generate_plots(&loaded_data, &param)?;
    let statistics = Statistics::calculate_statistics(&loaded_data);
    generate_html(&statistics, &param)?;

    info!("Done! Program execution duration: {:?}", start.elapsed());

    Ok(())
}

trait FileName {
    fn get_file_name(&self, extension: &str) -> String;
}

impl FileName for String {

    fn get_file_name(&self, extension: &str) -> String {
        let mut file_name = self.clone()
                .replace("\\", "-")
                .replace(" ", "_")
                .replace("/", "_")
                .replace("#", "_");
        file_name.push_str(extension);

        file_name
    }
}
