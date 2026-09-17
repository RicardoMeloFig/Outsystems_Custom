use std::time::Instant;
use std::collections::HashMap;
use log::{debug, info};
use serde_json::value::{Map, Value as Json};
use statrs::statistics::OrderStatistics;
use statrs::statistics::Mean;
use serde::Serialize;
use handlebars::to_json;

use crate::data_loader::LoadedData;
use crate::FileName;

pub struct Statistics {
    // Statistic -> {File : Data}
    stats: HashMap<String, HashMap<String, Stat>>
}

#[derive(Serialize, Debug)]
struct Stat {
    average: f64,
    median: f64,
    p25th_percentile: f64,
    p75th_percentile: f64,
    p90th_percentile: f64,
    p99th_percentile: f64
}

impl Statistics {

    pub fn jsonify(&self) -> Map<String, Json> {
        let mut data = Map::new();
        data.insert("metric".to_string(), to_json(&self.stats));

        data
    }

    pub fn calculate_statistics(loaded_data: &LoadedData) -> Statistics {
        let start = Instant::now();
        info!("Calculating statistics..");
    
        let mut statistics: HashMap<String, HashMap<String, Stat>> = HashMap::new();
    
        let distinct_metricts = loaded_data.get_distinct_metrics();
        for metric in distinct_metricts {
            statistics.insert(metric.get_file_name(".png"), HashMap::new());
    
            let files_contain_metric = loaded_data.get_files_that_contain_metric(&metric);
            for file_data in files_contain_metric {
                let file_values_for_metric = file_data.metrics.get(&metric).unwrap();
                let mut values = file_values_for_metric.data.clone();
    
                let stat = Stat {
                    average: values.mean(),
                    median: values.median(),
                    p25th_percentile: values.percentile(25),
                    p75th_percentile: values.percentile(75),
                    p90th_percentile: values.percentile(90),
                    p99th_percentile: values.percentile(99)
                };
    
                statistics.get_mut(&metric.get_file_name(".png")).unwrap().insert(file_data.file_name.clone(), stat);
            }
        }
    
        debug!("Sequencial statistics calculation (can be parallelized): {:?}", start.elapsed());
    
        Statistics {
            stats: statistics
        }
    }
}