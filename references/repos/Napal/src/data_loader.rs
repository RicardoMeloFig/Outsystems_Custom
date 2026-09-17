use std::collections::HashMap;
use std::collections::HashSet;
use anyhow::{Context, Result};
use log::{debug, info};
use std::fs::File;
use std::time::Instant;
use std::path::PathBuf;
use chrono::NaiveDateTime;

use crate::Parameters;

pub struct LoadedData {
    file_data: Vec<FileData>
}

pub struct FileData {
    pub metrics: HashMap<String, Metric<f64>>,
    pub timestamps: Metric<NaiveDateTime>,
    pub file_name: String
}

#[derive(Debug, Clone)]
pub struct Metric<T> {
    pub data: Vec<T>,
    pub name: String
}

impl LoadedData {
    
    pub fn get_distinct_metrics(&self) -> HashSet<String> {
        let mut all_metrics: HashSet<String> = HashSet::new();

        for data in &self.file_data {
            for metrics in &data.metrics {
                all_metrics.insert(metrics.0.clone());
            }
        }

        all_metrics
    }

    pub fn get_files_that_contain_metric(&self, metric: &str) -> Vec<&FileData> {
        let mut files_that_contain_metric = Vec::new();
        for file_data in self.get_all_data() {
            if file_data.contains_metric(metric) {
                files_that_contain_metric.push(file_data)
            }
        }

        files_that_contain_metric
    }

    pub fn get_all_data(&self) -> &Vec<FileData> {
        &self.file_data
    }

    pub fn load_file_data(cvs_file_list: &Vec<PathBuf>, param: &Parameters) -> Result<LoadedData> {
        let start = Instant::now();
        info!("Loading csv..");
    
        // Initialize Data
        let mut data: Vec<FileData> = Vec::new();
    
        for parsed_file in cvs_file_list.iter() {
            let mut index_column_map = HashMap::new();
            let mut columns: HashMap<String, Metric<f64>> = HashMap::new();
            let mut timestamps: Metric<NaiveDateTime> = Metric { data: Vec::new(), name: "date".to_string()};
            let parsed_csv_file = File::open(parsed_file)
                .with_context(|| format!("Could not open file {:?}", parsed_file))?;
    
            let mut cvs_reader = csv::ReaderBuilder::new()
                .has_headers(true)
                .from_reader(parsed_csv_file);
    
            let headers = cvs_reader.headers()
                .with_context(|| "Problem obtaining parsed csv headers")?;
            for (idx, column_name) in headers.iter().enumerate() {
                index_column_map.insert(idx, column_name.to_string());
                let metric_data = Metric { 
                    data: Vec::new(), 
                    name: column_name.to_string()
                };
    
                columns.insert(column_name.to_string(), metric_data);
            }
    
            // Load Data
            for record in cvs_reader.records() {
                let current_row = record.unwrap();
                for (column_idx, entry) in current_row.iter().enumerate() {
                    // First column is time, so handle it specially
                    if column_idx == 0 {
                        let time = NaiveDateTime::parse_from_str(entry, &param.data_time_format)
                            .with_context(|| format!("The time format {} did not work for the entry {}", &param.data_time_format, entry))?;
                        timestamps.data.push(time);
                    } else {
                        let column_name = &index_column_map.get(&column_idx).unwrap();
                        let column = columns.get_mut(*column_name).unwrap();
                        column.add(entry.parse::<f64>().unwrap_or(0.0));
                    }
                }
            }
    
            &columns.remove(&index_column_map[&0]);
    
            let file_data = FileData {
                metrics: columns,
                timestamps: timestamps,
                file_name: parsed_file.file_name().unwrap().to_string_lossy().to_string(),
            };
    
            data.push(file_data);
        }
    
        debug!("Sequencial Data loading duration: {:?}", start.elapsed());
        Ok(LoadedData { file_data: data })
    }
}


impl FileData {
    
    pub fn contains_metric(&self, name: &str) -> bool {
        return self.metrics.contains_key(name)
    }

}

impl<T> Metric<T> {
    fn add(&mut self, data: T) {
        self.data.push(data)
    }
}