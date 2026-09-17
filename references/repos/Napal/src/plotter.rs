
use std::time::Instant;
use std::path::Path;
use log::{debug, info};
use plotters::prelude::*;
use rayon::prelude::*;
use std::str::FromStr;
use std::num::ParseIntError;
use std::io::{self, BufRead};
use std::fs::File;
use anyhow::{Context, Result};

use crate::parameters::Parameters;
use crate::data_loader::{LoadedData, FileData};
use crate::FileName;
use crate::parameters::TimeFormat;


pub fn generate_plots(loaded_data: &LoadedData, param: &Parameters) -> Result<()> {
    let start = Instant::now();
    info!("Generating plots..");

    // For every metric, create a new graph with every file that has said metric.
    // Parallel!
    let plot_settings = get_settings(0, 0, param);
    loaded_data.get_distinct_metrics().par_iter().for_each(|metric| {

        let mut files_that_contain_metric = Vec::new();
        for file_data in loaded_data.get_all_data() {
            if file_data.contains_metric(&metric) {
                files_that_contain_metric.push(file_data)
            }
        }

        match create_plot(files_that_contain_metric, metric.clone(), param, &plot_settings) {
            Err(e) => panic!("{:?}", e),
            Ok(_) => (),
        }
    });


    debug!("Parallel Generate plots duration: {:?}", start.elapsed());
    
    Ok(())
}

fn create_plot(file_datas: Vec<&FileData>, metric: String, param: &Parameters, plot_settings: &PlotterSettings) -> Result<()> {
    debug!("Creating plot for {}", metric);

    // Image filename
    let base_path = Path::new(&param.target_directory);
    let image_path = base_path.join(metric.get_file_name(".png"));

    // Get longest duration - X axis
    let mut max_timestamp: usize = 0;
    for file_data in &file_datas {
        let initial_time = *file_data.timestamps.data.first().unwrap();
        let last_time = *file_data.timestamps.data.last().unwrap();
        let duration = last_time.signed_duration_since(initial_time);
        let timestamp_value  = match &param.x_axis {
            TimeFormat::Seconds => duration.num_seconds(),
            TimeFormat::Minutes => duration.num_minutes(),
        };
        if timestamp_value as usize > max_timestamp {
            max_timestamp = timestamp_value as usize;
        }
    } 

    // Get highest value -  Y Axis
    let mut max_value: f64 = 0.0;
    let mut max_amount_values: u32 = 0;
    for file_data in &file_datas {
        let metric_data = &file_data.metrics[&metric].data;
        let current_max = metric_data.iter().cloned().fold(-1./0. /* -inf */, f64::max);
        if current_max > max_value {
            max_value = current_max;
        }

        let current_amount_max: u32 = metric_data.len() as u32;
        if current_amount_max > max_amount_values {
            max_amount_values = current_amount_max;
        }
    }

    // Create base chart based on stats from each file
    //let root = SVGBackend::new(&image_path, (max_amount_values * param.width_per_point, 768)).into_drawing_area();
    let width = std::cmp::max(plot_settings.minimum_width, max_amount_values * param.width_per_point);
    let root = BitMapBackend::new(&image_path, (width, 768)).into_drawing_area();
    root.fill(&WHITE).with_context(|| "Filling plot color problems (weird...)")?;

    // TODO: CALCULAR VALORES

    let mut chart = ChartBuilder::on(&root)
        .x_label_area_size(plot_settings.x_label_area_size)
        .y_label_area_size(plot_settings.y_label_area_size)
        .caption(&metric, ("sans-serif", plot_settings.caption_size).into_font()) // Size of caption
        .build_ranged(0..max_timestamp, 0f64..max_value).with_context(|| "Building plot problems (weird...)")?;

    chart.configure_mesh()
        .x_desc("Time (seconds)")
        .y_desc("Value")
        .x_labels(plot_settings.x_labels) // Number of metrics on X axis
        .x_label_style(("sans-serif", plot_settings.x_label_style).into_font())
        .y_labels(plot_settings.y_labels) // Number of metrics on Y axis
        .y_label_style(("sans-serif", plot_settings.y_label_style).into_font())
       // .line_style_2(&WHITE)
        .draw().with_context(|| "Drawing plot problems (weird...)")?;

    let colors = get_colors(param, &plot_settings);

    for (idx, file_data) in file_datas.iter().enumerate() {
        let metric_data = &file_data.metrics[&metric].data;
        let timestamps = &file_data.timestamps.data;
        let file_name = &file_data.file_name;
        let first_timestamp = timestamps.first().unwrap();
        let colour = colors[idx].clone();


        chart.draw_series(LineSeries::new(
            timestamps.iter().zip(metric_data).map(|(time, value)| {
                let duration = time.signed_duration_since(*first_timestamp);
                let duration_value  = match &param.x_axis {
                    TimeFormat::Seconds => duration.num_seconds(),
                    TimeFormat::Minutes => duration.num_minutes(),
                };
                (duration_value as usize, *value) 
            }), colour.clone())).with_context(|| "Plot line drawing problems (weird...)")?
            .label(file_name)
            .legend(move |(x, y)| 
                (PathElement::new(vec![(x, y), (x + 20, y)], colour.clone())));
    }

    chart.configure_series_labels()
        .label_font(("sans-serif", plot_settings.legend_label_font).into_font())
        .background_style(&WHITE.mix(0.8))
        .border_style(&BLACK)
        .draw().with_context(|| "Final plot building step problems (weird...)")?;

    Ok(())
}



fn get_colors(param: &Parameters, plot_settings: &PlotterSettings) -> Vec<ShapeStyle> {
    let filled = true;
    let stroke_width = plot_settings.stroke_width;
    let mut result = Vec::new();
    let mut file_colors = Vec::new();

    let file = File::open(&param.plotter_colors_file).unwrap();
    let reader = io::BufReader::new(file).lines();
    for result_line in reader {
        if let Ok(line) = result_line {
            if line.is_empty() {
                continue;
            }
            file_colors.push(line.parse::<RGB>().unwrap());
        }
    }

    for rgb in file_colors {
        let shape_style = ShapeStyle { color: RGBColor(rgb.0, rgb.1, rgb.2).to_rgba(), filled, stroke_width };
        result.push(shape_style);
    }

    result
}


pub struct RGB(pub u8, pub u8, pub u8);

impl FromStr for RGB {
    type Err = ParseIntError;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        let rgb: Vec<u8> = s.split(',')
                                 .map(|s| s.trim())
                                 .map(|s| s.parse::<u8>().unwrap())
                                 .collect();

        Ok(RGB(rgb[0], rgb[1], rgb[2]))
    }
}

fn get_settings(_width: usize, _height: usize, param: &Parameters) -> PlotterSettings {
    // Initialize with default values
    let mut plot_settings = PlotterSettings {
        // Plot size
        minimum_width: 1500,
        // Labels
        caption_size: 50,      // Graph label
        x_label_area_size: 70, // X label size
        y_label_area_size: 100, // Y label size
        // Ticks
        x_labels: 10,         // X axis number of values
        x_label_style: 20,    // X axis value size
        y_labels: 10,         // Y axis number of values
        y_label_style: 20,    // Y axis value size
        // File to line legend label
        legend_label_font: 20,
        // Width of each line
        stroke_width: 2
    };

    let file = File::open(&param.plotter_config_file).with_context(|| format!("Could not open file {:?}", param.plotter_config_file)).unwrap();
    let reader = io::BufReader::new(file).lines();
    for result_line in reader {
        if let Ok(line) = result_line {
            if line.is_empty() {
                continue;
            }
            if line.contains("//") {
                continue;
            }

            let config: Vec<&str> = line.split(':')
                .map(|s| s.trim())
                .collect();
            
            match config[0] {
                "minimum_width" => plot_settings.minimum_width = config[1].parse::<u32>().unwrap(),
                "caption_size" => plot_settings.caption_size = config[1].parse::<u32>().unwrap(),
                "x_label_area_size" => plot_settings.x_label_area_size = config[1].parse::<u32>().unwrap(),
                "y_label_area_size" => plot_settings.y_label_area_size = config[1].parse::<u32>().unwrap(),

                "x_labels" => plot_settings.x_labels = config[1].parse::<usize>().unwrap(),
                "x_label_style" => plot_settings.x_label_style = config[1].parse::<u32>().unwrap(),
                "y_labels" => plot_settings.y_labels = config[1].parse::<usize>().unwrap(),
                "y_label_style" => plot_settings.y_label_style = config[1].parse::<u32>().unwrap(),

                "legend_label_font" => plot_settings.legend_label_font = config[1].parse::<u32>().unwrap(),
                "stroke_width" => plot_settings.stroke_width = config[1].parse::<u32>().unwrap(),
                _ => ()
            }
        }
    }

    debug!("Plot settings: {:?}", plot_settings);

    plot_settings
}
#[derive(Debug)]
struct PlotterSettings {
    // Plot size
    minimum_width: u32,
    // Labels
    caption_size: u32,      // Graph label
    x_label_area_size: u32, // X label size
    y_label_area_size: u32, // Y label size
    // Ticks
    x_labels: usize,         // X axis number of values
    x_label_style: u32,    // X axis value size
    y_labels: usize,         // Y axis number of values
    y_label_style: u32,    // Y axis value size
    // File to line legend label
    legend_label_font: u32,
    // Width of each line
    stroke_width: u32
}