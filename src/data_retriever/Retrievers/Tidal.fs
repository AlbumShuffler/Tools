module AlbumShuffler.DataRetriever.Retrievers.Tidal

open System
open System.Net.Http
open System.Threading.Tasks
open AlbumShuffler.Shared
open FsToolkit.ErrorHandling

type TidalConfig = {
    ClientId: string
    ClientSecret: string
}

(*
let retrieveDataForArtist ()
let retriever (config: TidalConfig) (s: Inputs.Source) : TaskResult<Intermediate.Artist * Outputs.Audiobook list, string> =
    taskResult {
        return failwith "rekt"
    }
*)