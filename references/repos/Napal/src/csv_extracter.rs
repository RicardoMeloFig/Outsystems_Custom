use anyhow::{Context, Result};
use log::{debug, info};
use std::io::{self, BufRead};
use std::fs::File;
use std::path::PathBuf;
use std::time::Instant;
use rayon::prelude::*;

use crate::parameters::Parameters;

struct MetricRules {
    metrics: Vec<String>,
    ignore: Vec<String>
}


pub fn extract_columns_base(file_list: &Vec<PathBuf>, parsed_file_list: &Vec<PathBuf>, param: &Parameters) {
    if !param.skip_parse {
        debug!("Parallel Parsing files: {:?}", &file_list);
        info!("Parsing csv..");
        let start = Instant::now();

        let rules = extract_metric_rules(&param.wanted_metrics_file).unwrap();

        file_list.par_iter()
            .zip(parsed_file_list)
            .for_each(|(file_name, altered_file_name)| {
                match extract_columns(&file_name, &altered_file_name, &rules) {
                    Err(e) => panic!("{:?}", e),
                    Ok(_) => (),
                }
            });

        debug!("TOTAL extraction duration: {:?}", start.elapsed());
    }
}

fn extract_metric_rules(wanted_metrics_location: &PathBuf) -> Result<MetricRules> {
    let mut metrics: Vec<String> = Vec::new();
    let mut ignore: Vec<String> = Vec::new();
    let file = File::open(wanted_metrics_location)
        .with_context(|| format!("Could not open file {:?}", wanted_metrics_location))?;
    let reader = io::BufReader::new(file).lines();

    let mut ignore_metric_flag = false;
    for result_line in reader {
        if let Ok(line) = result_line {
            if line.is_empty() {
                continue;
            }
            if line == "#$%#$%THIS_IS_THE_SEPARATOR. UP ARE WANTED METRICS, BELOW ARE IGNORED METRICS." {
                ignore_metric_flag = true;
            } else {
                if ignore_metric_flag {
                    ignore.push(line);
                } else {
                    metrics.push(line);
                }
            }
        }
    }

    debug!("Looking for metrics that contain: {:?}", metrics);
    debug!("Ignoring any metrics that contain {:?}", ignore);

    Ok(MetricRules {
        metrics,
        ignore,
    })
}

fn extract_columns(original_csv_name: &PathBuf, parsed_csv_name: &PathBuf, rules: &MetricRules) -> Result<()> {
    let start = Instant::now();

    let original_csv_file = File::open(original_csv_name)
        .with_context(|| format!("Could not open file {:?}", original_csv_name))?;
    let parsed_csv_file = File::create(parsed_csv_name)
        .with_context(|| format!("Could not create file {:?}", parsed_csv_name))?;

    let mut rdr = csv::ReaderBuilder::new()
        .has_headers(false)
        .from_reader(original_csv_file);
    let headers = rdr.headers()
        .with_context(|| format!("File {:?} has some csv issues", original_csv_name))?;

    // Store into vector the indexes of the wanted headers. We add 0 because it is the time header
    let mut relevant_idxs: Vec<usize> = vec![0];

    for (idx, header) in headers.iter().enumerate() {
        if rules.metrics.iter().any(|metric| header.contains(metric)) {
            if !rules.ignore.iter().any(|ignore| header.contains(ignore)) {
                &relevant_idxs.push(idx);
            }
        }
    }
    debug!("Relevant idxs are {:?}", relevant_idxs);

    let mut writer = csv::Writer::from_writer(&parsed_csv_file);
    for record in rdr.records() {
        let mut row_to_add: Vec<String> =  Vec::new();
        let current_row = match record {
            Err(_) => continue,
            Ok(str) => str.clone()
        };

        for i in &relevant_idxs {
            &row_to_add.push(current_row[*i].to_string());
        }
        writer.write_record(row_to_add)
            .with_context(|| format!("There was an issue writing to file {:?}", &parsed_csv_file))?;
    }

    debug!("{} column extraction duration: {:?}", original_csv_name.to_string_lossy(), start.elapsed());
    Ok(())
}