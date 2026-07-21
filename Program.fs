module laundry_notifier.Program

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.Configuration

type Config =
    { DiscordWebhookUrl: string
      WasherUri: string
      DryerUri: string }

type ProgramEnd = { End: string }

type DeviceStatus =
    { DeviceName: string
      Serial: string
      Inactive: string
      Program: string
      Status: string
      ProgramEnd: ProgramEnd }

let loadConfig () =
    ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json")
        .AddEnvironmentVariables()
        .Build()
        .Get<Config>()

let http = new HttpClient(Timeout = TimeSpan.FromSeconds 30.0)

let getStatus (baseUri: string) =
    task {
        let! json = http.GetStringAsync($"{baseUri}/ai?command=getDeviceStatus")
        return JsonSerializer.Deserialize<DeviceStatus>(json)
    }

let sendDiscord (webhookUrl: string) (name: string) (program: string) (status: string) =
    task {
        let embed =
            {| title = $"🧺 {name} finished"
               description = $"**{program}** has ended\n{status}"
               color = 5763719 // green
               timestamp = DateTime.UtcNow.ToString "o" |}

        let payload = JsonSerializer.Serialize {| embeds = [| embed |] |}
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
                let! deviceStatus = getStatus uri

                let isInactiveOrWithoutEndTime =
                    deviceStatus.Inactive = "true" || deviceStatus.Status = "Determining load\n"

                if isInactiveOrWithoutEndTime then
                    do! Task.Delay(TimeSpan.FromMinutes 10.0)
                else
                    printfn $"{name}: '{deviceStatus.Program}' running, ends in {deviceStatus.ProgramEnd.End}"
                    do! Task.Delay(parseRemaining deviceStatus.ProgramEnd.End)

                    let program = deviceStatus.Program
                    let mutable ended = false

                    while not ended do
                        try
                            let! s = getStatus uri

                            if s.ProgramEnd.End = "" then
                                ended <- true
                                do! sendDiscord config.DiscordWebhookUrl name program s.Status
                            else
                                do! Task.Delay(TimeSpan.FromMinutes 1.0)
                        with ex ->
                            eprintfn $"{name}: {ex.Message}"
                            do! Task.Delay(TimeSpan.FromSeconds 5.0)
            with ex ->
                eprintfn $"{name}: {ex.Message}"
                do! Task.Delay(TimeSpan.FromSeconds 5.0)
    }

[<EntryPoint>]
let main _ =
    let config = loadConfig ()

    [| monitor config "Washer" config.WasherUri
       monitor config "Dryer" config.DryerUri |]
    |> Task.WhenAll
    |> _.Wait()

    0
