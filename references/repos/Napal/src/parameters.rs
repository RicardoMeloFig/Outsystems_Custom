use std::env;
use chrono::{Datelike, Timelike, Utc};
use env_logger::Builder;
use log::{LevelFilter, debug, error, info};
use std::fs;
use std::path::PathBuf;
use anyhow::{Context, Result};

pub enum TimeFormat {
    Seconds,
    Minutes
}
pub struct Parameters {
    pub base_directory: PathBuf,
    pub skip_parse: bool,
    pub width_per_point: u32,
    pub target_directory: PathBuf,
    pub x_axis: TimeFormat,
    pub data_time_format: String,
    pub wanted_metrics_file: PathBuf,
    pub plotter_config_file: PathBuf,
    pub plotter_colors_file: PathBuf
}

static WANTED_METRICS_DEFAULT_PATH: &str = "config/DefaultMetrics.txt";
static PLOTTER_CONFIG_DEFAULT_PATH: &str = "config/DefaultPlotSettings.txt";
static PLOTTER_COLORS_DEFAULT_PATH: &str = "config/DefaultPlotLineColors.txt";

impl Parameters {

    fn new(base_directory: PathBuf, skip_parse: bool, width_per_point: u32, target_directory: PathBuf, wanted_metrics_file: &String, 
        x_axis: TimeFormat, data_time_format: String, plotter_config_file: &String, plotter_colors_file: &String) -> Result<Parameters, Box<dyn std::error::Error>> {
        fs::create_dir_all(&target_directory)
            .with_context(|| format!("Could not create directory {:?}", &target_directory))?;

        let verified_plotter_config_file = verify_file_exists(plotter_config_file);
        let verified_plotter_colors_file = verify_file_exists(plotter_colors_file);
        let verified_wanted_metrics_file = verify_file_exists(wanted_metrics_file);
        
        Ok(Parameters {
            base_directory,
            skip_parse,
            width_per_point,
            target_directory,
            wanted_metrics_file: verified_wanted_metrics_file,
            x_axis,
            data_time_format,
            plotter_config_file: verified_plotter_config_file,
            plotter_colors_file: verified_plotter_colors_file,
        })
    }

    #[cfg(not(debug_assertions))]
    fn get_base_path() -> PathBuf {
        let exe_path = env::current_exe().unwrap();
        let parent_exe_path = exe_path.parent().unwrap();
        parent_exe_path.to_owned()
    }

    #[cfg(debug_assertions)]
    fn get_base_path() -> PathBuf {
        PathBuf::new()
    }

    pub fn obtain() -> (Parameters, Vec<PathBuf>) {
        let base_path = Parameters::get_base_path();

        let mut skip_parse = false;
        let mut width_per_point = 1;
        let now = Utc::now();
        // Default target directory is based on time
        let mut target_directory = PathBuf::from("results/").join(format!("{}-{}-{}_{}-{}-{}", now.year(), now.month(), now.day(), now.hour(), now.minute(), now.second()));
        let mut data_time_format = "%m/%d/%Y %H:%M:%S.%f".to_string();
        let mut file_list: Vec<String> = Vec::new();
        let mut x_axis = TimeFormat::Seconds;
        let mut wanted_metrics_file: &String = &WANTED_METRICS_DEFAULT_PATH.to_string();
        let mut plotter_config_file: &String = &PLOTTER_CONFIG_DEFAULT_PATH.to_string();
        let mut plotter_colors_file: &String = &PLOTTER_COLORS_DEFAULT_PATH.to_string();
        let mut verbose = false;
    
        let args: Vec<String> = env::args().skip(1).collect();

        if args.len() == 0 {
            Parameters::help();
            std::process::exit(0) 
        }
    
        let mut i = 0;
        while i < args.len() {
            let current_arg = &args[i];

            match current_arg.to_lowercase().as_str() {
                "-s" | "-skipparse" => skip_parse = true,
                "-w" | "-widthperpoint" => { 
                    width_per_point = args.get(i + 1).unwrap().parse::<u32>().unwrap();
                    i += 1
                }
                "-t" | "-targetdir" => {
                    target_directory = PathBuf::from(args.get(i + 1).unwrap().clone());
                    i += 1
                }
                "-tf" | "-timeformat" => {
                    data_time_format = args.get(i + 1).unwrap().clone();
                    i += 1
                }
                "-ps" | "-plotsettings" => {
                    plotter_config_file = args.get(i + 1).unwrap();
                    i += 1

                }
                "-c" | "-colorsfile" => {
                    plotter_colors_file = args.get(i + 1).unwrap();
                    i += 1
                }
                "-wm" | "-wantedmetrics" => {
                    wanted_metrics_file = args.get(i + 1).unwrap();
                    i += 1
                }
                "-v" | "-verbose" => {
                    verbose = true;
                }
                "-h" | "-help" => {
                    Parameters::help();
                    std::process::exit(0) 
                }
                "-xaxis" => {
                    let time_format_arg = args.get(i + 1).unwrap().clone();
                    x_axis = match time_format_arg.to_lowercase().as_str() {
                        "seconds" => TimeFormat::Seconds,
                        "minutes" => TimeFormat::Minutes,
                        _ => panic!("Wrong time format. Options are <seconds> or <minutes>")
                    };
                    i += 1    
                }

                _ => file_list.push(current_arg.to_owned())
            }
    
            i += 1;
        }

        Builder::new()
            .format_timestamp(Option::None)
            .format_module_path(false)
            .filter_level(if verbose { LevelFilter::max()} else { LevelFilter::Info}).init();

        let param = Parameters::new(
            base_path,
            skip_parse, 
            width_per_point, 
            target_directory, 
            wanted_metrics_file, 
            x_axis, 
            data_time_format,
            plotter_config_file,
            plotter_colors_file,
        ).unwrap();

        let verified_file_list = file_list.into_iter().map(|file_string| verify_file_exists(&file_string)).collect::<Vec<PathBuf>>();
    
        (param, verified_file_list)
    }

    pub fn help() {
        println!("{}", r"
Napal

A faster Performance Analysis of Logs

This is a tool that analyses .csv files and generates plots. The current use is for analyzing performance counter reports from Windows, but it's generic enough for other tasks.


Examples:
    .\napal.exe testfile.csv
        This uses the default metrics, plot settings and plot colors, located in the config/ directory.

    .\napal.exe -v -wm specific-metrics.txt testfile.csv
        This will print debug information, use the metrics located in the specific-metrics.txt file and use the plot settings and plot colors located in the config/ directory

    .\napal.exe testfile1.csv testfile2.csv
        This will use the default settings and create plots with two lines. This allows the comparison of different executions of the same thing.

        
Parameters:

[] are optional arguments
() Represents options
<> are obligatory

<csv files>
    The list of files that will be parsed and used to create plots.
    This is mandatory to be the last argument.

[-t or -targetDir]
    The directory where the results will be saved
    Default directory is results/{year}-{month}-{day}_{hour}-{minute}-{second}
        Example: results/2021-05-18_10-10-10
        Do note that the results/dir will be placed in the working directory.

[-wm or -wantedMetrics]
    The path for the file that contains which metrics are desired to be analyzed.
    Default is config/DefaultMetrics.txt.

[-ps or -plotSettings]
    The path for the file that contains the settings to be used when plotting
    Default is config/DefaultPlotSettings.txt.

[-c or -colorsFile]
    The path for the file that contains a list of the colors to be used in the graphs (by order)
    Default is config/DefaultPlotLineColors.txt.

[-w or -widthPerPoint]
    The width of the generated image per given point in each graph (x axis).
    Default is 1.

[-s or -skipParse]
    Whether the .csv files should be parsed or not. It is required for them to be parsed at least once, so that they generate the .altered.csv file.
    Default is false.

[-tf or -timeFormat]
    The format for the date column. How to write the format: https://docs.rs/chrono/0.4.7/chrono/format/strftime/index.html
    Default is %m/%d/%Y %H:%M:%S.%f 
        Example date: 05/02/2020 15:30:10.012.

[-xaxis (seconds|minutes)]
    Whether the X axis of the plots should be in seconds or minutes.
    Default is seconds.
    Example: 
        .\napal.exe -xaxis seconds testfile1.csv
        .\napal.exe -xaxis minutes testfile1.csv

[-v or -verbose]
    Whether to display debug information.
    Default is to not display.

[-h or -help]
    Displays this information.        
        ");
    }

    pub fn print(&self) {
        if self.skip_parse {
            debug!("Skipping file parsing");
        } else {
            debug!("Will parse the files")
        }

        info!("Files: ");
        info!("     The analysed metrics file is {:?}.", self.wanted_metrics_file);
        info!("     The Plot config file is {:?}.", self.plotter_config_file);
        info!("     The colors file is {:?}.", self.plotter_colors_file);
        info!("     Target directory is {:?}.", self.target_directory);
        info!("Other configs:");
        info!("     Width per point is {}.", self.width_per_point);
        info!("     The data time format is {}.", self.data_time_format);
        match self.x_axis {
            TimeFormat::Seconds => info!("     Plot X axis will be in seconds."),
            TimeFormat::Minutes => info!("     Plot X axis will be in minutes.")
        }
        info!("");
    }
}

// Verify if file exists in sent path,
// Otherwise check if file exists relative to location of executable
// Otherwise check if file exists 2 paths backwards (for development)
pub fn verify_file_exists(file_path: &String) -> PathBuf {
    let mut possible_path = PathBuf::from(file_path);

    if possible_path.exists() {
        return possible_path;
    }

    possible_path = env::current_exe().unwrap().parent().unwrap().join(&file_path);

    if possible_path.exists() {
        return possible_path;
    }

    possible_path = env::current_exe().unwrap().parent().unwrap().parent().unwrap().parent().unwrap().join(&file_path);

    // For development, assumes exe is in target/{debug/release}
    if possible_path.exists() {
        return possible_path;
    }

    error!("The file {:?} does not exist", &file_path);
    std::process::exit(0) 
}