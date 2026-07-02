module laundry_notifier.Program

open System
open System.IO
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks

[<CLIMutable>]
type Config =
    { DiscordWebhookUrl: string
      WasherUri: string
      DryerUri: string }

[<CLIMutable>]
type ProgramEnd =
    { End: string
      EndType: string }

[<CLIMutable>]
type DeviceStatus =
    { DeviceName: string
      Serial: string
      Inactive: string
      Program: string
      Status: string
      ProgramEnd: ProgramEnd
      deviceUuid: string }

let loadConfig () =
    let path = Path.Combine(AppContext.BaseDirectory, "appsettings.json")
    JsonSerializer.Deserialize<Config>(File.ReadAllText path)

let http = new HttpClient(Timeout = TimeSpan.FromSeconds 30.0)

let getStatus (baseUri: string) =
    task {
        let! json = http.GetStringAsync($"{baseUri}/ai?command=getDeviceStatus")
        return JsonSerializer.Deserialize<DeviceStatus>(json)
    }

let sendDiscord (webhookUrl: string) (message: string) =
    task {
        let payload = JsonSerializer.Serialize {| content = message |}
        use content = new StringContent(payload, Encoding.UTF8, "application/json")
        let! _ = http.PostAsync(webhookUrl, content)
        ()
    }

// "0h19" -> 19 minutes
let parseRemaining (s: string) =
    match s.Split 'h' with
    | [| h; m |] -> TimeSpan(int h, int m, 0)
    | _ -> TimeSpan.FromMinutes 1.0

let monitor (config: Config) (name: string) (uri: string) =
    task {
        while true do
            try
                let! status = getStatus uri

                if status.Inactive = "true" then
                    do! Task.Delay(TimeSpan.FromMinutes 15.0)
                else
                    // a wash has started: wait until the scheduled completion time
                    printfn $"{name}: '{status.Program}' running, ends in {status.ProgramEnd.End}"
                    do! Task.Delay(parseRemaining status.ProgramEnd.End)

                    let mutable program = status.Program
                    let mutable lastStatus = status.Status
                    let mutable ended = false

                    while not ended do
                        try
                            let! s = getStatus uri

                            if s.ProgramEnd.End = "" then
                                ended <- true
                                do! sendDiscord config.DiscordWebhookUrl $"{name} {program} has ended: {lastStatus}"
                            else
                                if s.Program <> "" then program <- s.Program
                                if s.Status <> "" then lastStatus <- s.Status
                                do! Task.Delay(TimeSpan.FromMinutes 1.0)
                        with ex ->
                            // device can answer 503 while busy; keep polling
                            eprintfn $"{name}: {ex.Message}"
                            do! Task.Delay(TimeSpan.FromMinutes 1.0)
            with ex ->
                eprintfn $"{name}: {ex.Message}"
                do! Task.Delay(TimeSpan.FromMinutes 15.0)
    }

[<EntryPoint>]
let main _ =
    let config = loadConfig ()

    [| monitor config "Washer" config.WasherUri
       monitor config "Dryer" config.DryerUri |]
    |> Task.WhenAll
    |> fun t -> t.Wait()

    0
