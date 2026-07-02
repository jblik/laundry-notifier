module laundry_notifier.Program

open System
open System.IO
open System.Text.Json

[<CLIMutable>]
type Config =
    { DiscordWebhookUrl: string
      WasherUri: string
      DryerUri: string }

let loadConfig () =
    let path = Path.Combine(AppContext.BaseDirectory, "appsettings.json")
    JsonSerializer.Deserialize<Config>(File.ReadAllText path)

[<EntryPoint>]
let main _ =
    let config = loadConfig ()
    printfn $"Washer: {config.WasherUri}, Dryer: {config.DryerUri}"
    0
