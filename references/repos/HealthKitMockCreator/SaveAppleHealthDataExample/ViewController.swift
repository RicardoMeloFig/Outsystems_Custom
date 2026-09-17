//
//  ViewController.swift
//  SaveAppleHealthDataExample
//
//  Created by Domo on 03/12/2019.
//  Copyright © 2019 Domo. All rights reserved.
//

import UIKit
import HealthKit

class ViewController: UIViewController {
    
    @IBOutlet weak var textFieldValue: UITextField!
    @IBOutlet weak var datePicker: UIDatePicker!
    
    override func viewDidLoad() {
        super.viewDidLoad()
        // Do any additional setup after loading the view.
        
        HealthKitSetupAssistant.authorizeHealthKit { (result, error) in
            if result {
                print("Auth ok")
            } else {
                print("Auth denied")
            }
        }
        
    }
    
    @IBAction func save4000StepsToday(_ sender: Any) {
        let value = Int.random(in: 1...4000)
        let today = Date()
        
        HealthKitSetupAssistant.saveSteps(stepsCountValue: value, date: today) { (error) in
            print(error)
        }
        
    }
    
    @IBAction func save8000StepsToday(_ sender: Any) {
        let value = Int.random(in: 4000...8000)
        let today = Date()
        
        HealthKitSetupAssistant.saveSteps(stepsCountValue: value, date: today) { (error) in
            print(error)
        }
        
    }
    
    @IBAction func saveManyStepsToday(_ sender: Any) {
        let value = Int.random(in: 9000...20000)
        let today = Date()
        
        HealthKitSetupAssistant.saveSteps(stepsCountValue: value, date: today) { (error) in
            print(error)
        }
        
    }
    
    @IBAction func saveVO2Now(_ sender: Any) {
        let value = Int.random(in: 30...60)
        let today = Date()
        
        HealthKitSetupAssistant.saveVO2(vo2Value: Double(value), date: today) { (error) in
            print(error)
        }
        
    }
    
    @IBAction func saveHearthNow(_ sender: Any) {
        let value = Int.random(in: 100...190)
        let today = Date()
        
        HealthKitSetupAssistant.saveHeartRate(heartRateValue: value, date: today) { (error) in
            print(error)
        }
        
    }
    
    
    @IBAction func saveMeasurementsNow(_ sender: Any) {
        let value = Int.random(in: 100...300)
        let today = Date()
        
        HealthKitSetupAssistant.saveBodyMass(bodyMassValue: Double(value), date: today) { (error) in
            print(error)
        }
        
    }
    
    @IBAction func addHeartBeatsToLast24Hours(_ sender: Any) {
        var date = Calendar.current.date(byAdding: .hour, value: -24, to: Date())!
        let endDate = Date() // last date

        // Formatter for printing the date, adjust it according to your needs:
        let fmt = DateFormatter()
        fmt.dateFormat = "dd/MM/yyyy hh:mm"

        while date <= endDate {
            let value = Int.random(in: 50...80)
            
            HealthKitSetupAssistant.saveHeartRate(heartRateValue: value, date: date) { (error) in
                print(error)
            }
            print(fmt.string(from: date))
            date = Calendar.current.date(byAdding: .minute, value: 30, to: date)!
        }
        
    }
    
    
    @IBAction func addHeartBeatsToLastWeek(_ sender: Any) {
        var date = Calendar.current.date(byAdding: .day, value: -7, to: Date())!
        let endDate = Date() // last date

        // Formatter for printing the date, adjust it according to your needs:
        let fmt = DateFormatter()
        fmt.dateFormat = "dd/MM/yyyy hh:mm"

        while date <= endDate {
            let value = Int.random(in: 50...80)
            
            HealthKitSetupAssistant.saveHeartRate(heartRateValue: value, date: date) { (error) in
                print(error)
            }
            print(fmt.string(from: date))
            date = Calendar.current.date(byAdding: .minute, value: 30, to: date)!
        }
        
    }
    
    
    @IBAction func addHeartBeatsToLastMonth(_ sender: Any) {
        var date = Calendar.current.date(byAdding: .month, value: -72, to: Date())!
        let endDate = Date() // last date

        // Formatter for printing the date, adjust it according to your needs:
        let fmt = DateFormatter()
        fmt.dateFormat = "dd/MM/yyyy hh:mm"

        while date <= endDate {
            let value = Int.random(in: 50...80)
            
            HealthKitSetupAssistant.saveHeartRate(heartRateValue: value, date: date) { (error) in
                print(error)
            }
            print(fmt.string(from: date))
            date = Calendar.current.date(byAdding: .minute, value: 30, to: date)!
        }
        
    }
    
    
    @IBAction func addStepsToLastMonth(_ sender: Any) {
        var date = Calendar.current.date(byAdding: .month, value: -72, to: Date())!
        let endDate = Date() // last date

        // Formatter for printing the date, adjust it according to your needs:
        let fmt = DateFormatter()
        fmt.dateFormat = "dd/MM/yyyy hh:mm"

        while date <= endDate {
            let value = Int.random(in: 1200...12000)
            
            HealthKitSetupAssistant.saveSteps(stepsCountValue: value, date: date) { (error) in
                print(error)
            }
            print(fmt.string(from: date))
            date = Calendar.current.date(byAdding: .day, value: 1, to: date)!
        }
        
    }
    
    
    @IBAction func addStepsToLastWeek(_ sender: Any) {
        var date = Calendar.current.date(byAdding: .day, value: -7, to: Date())!
        let endDate = Date() // last date

        // Formatter for printing the date, adjust it according to your needs:
        let fmt = DateFormatter()
        fmt.dateFormat = "dd/MM/yyyy hh:mm"

        while date <= endDate {
            let value = Int.random(in: 1200...12000)
            
            HealthKitSetupAssistant.saveSteps(stepsCountValue: value, date: date) { (error) in
                print(error)
            }
            print(fmt.string(from: date))
            date = Calendar.current.date(byAdding: .day, value: 1, to: date)!
        }
        
    }
    
    
    
}
