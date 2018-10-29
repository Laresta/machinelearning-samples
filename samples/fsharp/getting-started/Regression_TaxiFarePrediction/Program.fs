﻿module Regression_TaxiFarePrediction

open Microsoft.ML.Runtime.Api
open System
open System.Diagnostics
open System.IO
open System.Linq

open Microsoft.ML.Legacy;
open Microsoft.ML.Legacy.Models;
open Microsoft.ML.Legacy.Data;
open Microsoft.ML.Legacy.Transforms;
open Microsoft.ML.Legacy.Trainers;

open PLplot

let AppPath = Path.Combine(__SOURCE_DIRECTORY__, "../../../..")
let TrainDataPath= Path.Combine(AppPath, "datasets", "taxi-fare-train.csv")
let TestDataPath= Path.Combine(AppPath, "datasets", "taxi-fare-test.csv")
let ModelPath= Path.Combine(AppPath, "TaxiFareModel.zip")

[<CLIMutable>]
type TaxiTrip = {
    [<Column("0")>] VendorId : string
    [<Column("1")>] RateCode : string
    [<Column("2")>] PassengerCount : float32
    [<Column("3")>] TripTime : float32
    [<Column("4")>] TripDistance : float32
    [<Column("5")>] PaymentType : string
    [<Column("6")>] FareAmount : float32
} with static member Empty = {
            VendorId  = ""
            RateCode = ""
            PassengerCount = 0.0f
            TripTime = 0.0f
            TripDistance = 0.0f
            PaymentType = ""
            FareAmount = 0.0f
        }

[<CLIMutable>]
type TaxiTripFarePrediction = {
    FareAmount : float32
}

// type TaxiTrip() =
//     [<Column("0")>]
//     member val VendorId: string = "" with get, set

//     [<Column("1")>]
//     member val RateCode: string = "" with get, set

//     [<Column("2")>]
//     member val PassengerCount: float32 = 0.0f with get, set

//     [<Column("3")>]
//     member val TripTime: float32 = 0.0f with get, set

//     [<Column("4")>]
//     member val TripDistance: float32 = 0.0f with get, set

//     [<Column("5")>]
//     member val PaymentType: string = "" with get, set

//     [<Column("6")>]
//     member val FareAmount: float32 = 0.0f with get,set

// type TaxiTripFarePrediction() =
//     [<ColumnName("Score")>]
//     member val FareAmount: float32 = 0.0f with get, set

module TestTaxiTrips =
    let Trip1 = { TaxiTrip.Empty with
                        VendorId = "VTS"
                        RateCode = "1"
                        PassengerCount = 1.0f
                        TripDistance = 10.33f
                        PaymentType = "CSH"
                        FareAmount = 0.0f // predict it. actual = 29.5
                }


let Train() =
    // LearningPipeline holds all steps of the learning process: data, transforms, learners.
    let pipeline = LearningPipeline()

    // The TextLoader loads a dataset. The schema of the dataset is specified by passing a class containing
    // all the column names and their types.
    pipeline.Add (TextLoader(TrainDataPath).CreateFrom<TaxiTrip>(separator=','))

    // Transforms
    // When ML model starts training, it looks for two columns: Label and Features.
    // Label:   values that should be predicted. If you have a field named Label in your data type,
    //              no extra actions required.
    //          If you don't have it, like in this example, copy the column you want to predict with
    //              ColumnCopier transform:
    pipeline.Add(ColumnCopier(struct ("FareAmount", "Label")))

    // CategoricalOneHotVectorizer transforms categorical (string) values into 0/1 vectors
    pipeline.Add(CategoricalOneHotVectorizer("VendorId", "RateCode", "PaymentType"))

    // Features: all data used for prediction. At the end of all transforms you need to concatenate
    //              all columns except the one you want to predict into Features column with
    //              ColumnConcatenator transform:
    pipeline.Add(ColumnConcatenator("Features",
                    "VendorId",
                    "RateCode",
                    "PassengerCount",
                    "TripDistance",
                    "PaymentType"))
        //FastTreeRegressor is an algorithm that will be used to train the model.
    pipeline.Add(FastTreeRegressor())

    printfn "=============== Training model ==============="
    // The pipeline is trained on the dataset that has been loaded and transformed.
    let model = pipeline.Train<TaxiTrip, TaxiTripFarePrediction>()

    // Saving the model as a .zip file.
    model.WriteAsync(ModelPath) |> Async.AwaitTask |> Async.RunSynchronously

    printfn "=============== End training ==============="
    printfn "The model is saved to %s" ModelPath

    model

let Evaluate(model: PredictionModel<TaxiTrip, TaxiTripFarePrediction>) =

    // To evaluate how good the model predicts values, it is run against new set
    // of data (test data) that was not involved in training.
    let testData = TextLoader(TestDataPath).CreateFrom<TaxiTrip>(separator=',')

    // RegressionEvaluator calculates the differences (in various metrics) between predicted and actual
    // values in the test dataset.
    let evaluator = RegressionEvaluator()

    printfn "=============== Evaluating model ==============="

    let metrics = evaluator.Evaluate(model, testData)

    printfn "Rms = {metrics.Rms}, ideally should be around 2.8, can be improved with larger dataset"
    printfn "RSquared = {metrics.RSquared}, a value between 0 and 1, the closer to 1, the better"
    printfn "=============== End evaluating ==============="
    printfn ""

let GetDataFromCsv(dataLocation: string, numMaxRecords: int) =
    File.ReadAllLines(dataLocation)
        .Skip(1)
        .Select(fun x -> x.Split(','))
        .Select(fun x ->
            { TaxiTrip.Empty with
                VendorId = x.[0]
                RateCode = x.[1]
                PassengerCount = Single.Parse(x.[2])
                TripTime = Single.Parse(x.[3])
                TripDistance = Single.Parse(x.[4])
                PaymentType = x.[5]
                FareAmount = Single.Parse(x.[6])
            }
        )
        .Take(numMaxRecords)

let PaintChart(model: PredictionModel<TaxiTrip, TaxiTripFarePrediction>,
               testDataSetPath: string,
               numberOfRecordsToRead: int,
               args: string[]) =

    use pl = new PLStream()
    // use SVG backend and write to SineWaves.svg in current directory
    let chartFileName =
        if (args.Length = 1 && args.[0] = "svg") then
            pl.sdev("svg")
            let chartFileName = "TaxiRegressionDistribution.svg"
            pl.sfnam(chartFileName)
            chartFileName
        else
            pl.sdev("pngcairo")
            let chartFileName = "TaxiRegressionDistribution.png"
            pl.sfnam(chartFileName)
            chartFileName

    // use white background with black foreground
    pl.spal0("cmap0_alternate.pal")

    // Initialize plplot
    pl.init()

    // set axis limits
    let xMinLimit = 0.0
    let xMaxLimit = 40.0 //Rides larger than $40 are not shown in the chart
    let yMinLimit = 0.0
    let yMaxLimit = 40.0  //Rides larger than $40 are not shown in the chart
    pl.env(xMinLimit, xMaxLimit, yMinLimit, yMaxLimit, AxesScale.Independent, AxisBox.BoxTicksLabelsAxes)

    // Set scaling for mail title text 125% size of default
    pl.schr(0.0, 1.25)

    // The main title
    pl.lab("Measured", "Predicted", "Distribution of Taxi Fare Prediction")

    // plot open different colors
    // see http://plplot.sourceforge.net/examples.php?demo=02 for palette indices
    pl.col0(1)

    let totalNumber = numberOfRecordsToRead
    let testData = GetDataFromCsv(testDataSetPath, totalNumber).ToList()

    //This code is the symbol to paint
    let code = (char)9

    // plot open other color
    //pl.col0(9) //Light Green
    //pl.col0(4) //Red
    pl.col0(2) //Blue

    let mutable yTotal = 0.0
    let mutable xTotal = 0.0
    let mutable xyMultiTotal = 0.0
    let mutable xSquareTotal = 0.0

    for i in 0 .. testData.Count-1 do
        let farePrediction = model.Predict(testData.[i])

        let x = [| float testData.[i].FareAmount |]
        let y = [| float farePrediction.FareAmount |]

        //Paint a dot
        pl.poin(x, y, code)

        xTotal <- xTotal + float x.[0]
        yTotal <- yTotal + float y.[0]

        let multi = x.[0] * y.[0]
        xyMultiTotal <-  xyMultiTotal + multi

        let xSquare = x.[0] * x.[0]
        xSquareTotal <- xSquareTotal + xSquare

        let ySquare = y.[0] * y.[0]

        printfn "-------------------------------------------------"
        printfn "Predicted : {FarePrediction.FareAmount}"
        printfn "Actual:    {testData[i].FareAmount}"
        printfn "-------------------------------------------------"

    // Regression Line calculation explanation:
    // https://www.khanacademy.org/math/statistics-probability/describing-relationships-quantitative-data/more-on-regression/v/regression-line-example

    let minY = yTotal / float totalNumber
    let minX = xTotal / float totalNumber
    let minXY = xyMultiTotal / float totalNumber
    let minXsquare = xSquareTotal / float totalNumber

    let m = ((minX * minY) - minXY) / ((minX * minX) - minXsquare)

    let b = minY - (m * minX)

    //Generic function for Y for the regression line
    // y = (m * x) + b

    let x1 = 1.0
    //Function for Y1 in the line
    let y1 = (m * x1) + b

    let x2 = 39.0
    //Function for Y2 in the line
    let y2 = (m * x2) + b

    let xArray = [| x1; x2 |]
    let yArray = [| y1; y2 |]

    pl.col0(4)
    pl.line(xArray, yArray)

    // end page (writes output to disk)
    pl.eop()

    // output version of PLplot
    let verText = pl.gver()
    printfn "PLplot version %s" verText

    // Open Chart File In Microsoft Photos App (Or default app, like browser for .svg)

    printfn "Showing chart..."
    let chartFileNamePath = @".\" + chartFileName
    let p = new Process(StartInfo=ProcessStartInfo(chartFileNamePath, UseShellExecute = true))
    p.Start() |> ignore


// STEP 1: Create a model
let model = Train()

// STEP2: Test accuracy
Evaluate(model)

// STEP 3: Make a test prediction
let prediction = model.Predict(TestTaxiTrips.Trip1)
printfn "Predicted fare: {prediction.FareAmount:0.####}, actual fare: 29.5"

//STEP 4: Paint regression distribution chart for a number of elements read from a Test DataSet file
let args = Environment.GetCommandLineArgs().[1..]
PaintChart(model, TestDataPath, 100, args)

printfn "Press any key to exit.."
Console.ReadLine() |> ignore
