﻿using Backend.Entities;
using Backend.Entities.GraphNodes;
using Backend.Errors;
using Serilog;
using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend
{
    public static class SpotifyOperations
    {
        private static ILogger Logger { get; } = Log.ForContext("SourceContext", "SP");
        private static ISpotifyClient Spotify => ConnectionManager.Instance.Spotify;

        private static Func<object, bool> TrackIsValid { get; } = t => t is FullTrack ft && !ft.IsLocal && ft.IsPlayable;
        public static Track ToTrack(FullTrack track)
        {
            return new Track
            {
                Id = track.LinkedFrom == null ? track.Id : track.LinkedFrom.Id,
                Name = track.Name,
                DurationMs = track.DurationMs,
                Album = new Album
                {
                    Id = track.Album.Id,
                    Name = track.Album.Name,
                    ReleaseDate = track.Album.ReleaseDate,
                    ReleaseDatePrecision = track.Album.ReleaseDatePrecision,
                },
                Artists = track.Artists.Select(a => new Artist { Id = a.Id, Name = a.Name }).ToList(),
            };
        }
        private static Track ToTrack(SimpleTrack track, FullAlbum album)
        {
            return new Track
            {
                Id = track.LinkedFrom == null ? track.Id : track.LinkedFrom.Id,
                Name = track.Name,
                DurationMs = track.DurationMs,
                Album = new Album
                {
                    Id = album.Id,
                    Name = album.Name,
                    ReleaseDate = album.ReleaseDate,
                    ReleaseDatePrecision = album.ReleaseDatePrecision,
                },
                Artists = track.Artists.Select(a => new Artist { Id = a.Id, Name = a.Name }).ToList(),
            };
        }


        private static async Task<List<T>> GetAll<T>(Paging<T> page)
        {
            var all = new List<T>();
            await foreach (var item in Spotify.Paginate(page))
                all.Add(item);
            return all;
        }



        private static async Task<List<Track>> GetLikedTracks()
        {
            Logger.Information("Start fetching liked tracks");
            var page = await Spotify.Library.GetTracks(new LibraryTracksRequest { Limit = 50, Market = DataContainer.Instance.User.Country });
            var spotifyTracks = await GetAll(page);
            Logger.Information("Finished fetching liked tracks");
            var tracks = spotifyTracks.Where(t => TrackIsValid(t.Track)).Select(t => ToTrack(t.Track)).ToList();
            tracks.ForEach(t => t.IsLiked = true);
            return tracks;
        }
        private static async Task<List<Playlist>> GetCurrentUsersPlaylists()
        {
            Logger.Information("Start fetching users playlists");
            var page = await Spotify.Playlists.CurrentUsers(new PlaylistCurrentUsersRequest { Limit = 50 });
            var allPlaylists = await GetAll(page);
            Logger.Information("Finished fetching users playlists");
            return allPlaylists.Select(p => new Playlist
            {
                Id = p.Id,
                Name = p.Name,
            }).ToList();
        }


        public static async Task<List<Track>> GetPlaylistTracks(string playlistId)
        {
            if (string.IsNullOrEmpty(playlistId)) return new();

            Logger.Information($"Start fetching playlist items playlistId={playlistId}");
            try
            {
                var request = new PlaylistGetItemsRequest { Limit = 100, Offset = 0, Market = DataContainer.Instance.User.Country };
                request.Fields.Add(
                    "items(" +
                        "type," +
                        "track(" +
                            "type," +
                            "id," +
                            "name," +
                            "duration_ms," +
                            "is_local," +
                            "is_playable," +
                            "linked_from," +
                            "album(id,name,release_date,release_date_precision)," +
                            "artists(id,name)" +
                        ")" +
                    ")," +
                    "next");
                var page = await Spotify.Playlists.GetItems(playlistId, request);
                var allTracks = await GetAll(page);
                Logger.Information($"Finished fetching playlist items playlistId={playlistId}");
                return allTracks.Where(t => TrackIsValid(t.Track)).Select(playlistTrack => ToTrack(playlistTrack.Track as FullTrack)).ToList();
            }
            catch (Exception e)
            {
                Logger.Error($"Error in PlaylistItems: {e.Message}");
                return new();
            }
        }
        public static async Task<Track> GetTrack(string id)
        {
            try
            {
                var fullTrack = await Spotify.Tracks.Get(id, new TrackRequest { Market = DataContainer.Instance.User.Country });
                return ToTrack(fullTrack);
            }
            catch (Exception e)
            {
                Logger.Error($"Error in {nameof(GetTrack)}: {e.Message}");
                return null;
            }
        }
        public static async Task<List<Track>> GetAlbumTracks(string id)
        {
            try
            {
                var album = await Spotify.Albums.Get(id, new AlbumRequest { Market = DataContainer.Instance.User.Country });
                var simpleTracks = await GetAll(album.Tracks);

                return simpleTracks.Select(t => ToTrack(t, album)).ToList();
            }
            catch (Exception e)
            {
                Logger.Error($"Error in {nameof(GetAlbumTracks)}: {e.Message}");
                return new();
            }
        }
        public static async Task<List<AudioFeatures>> GetAudioFeatures(List<string> trackIds)
        {
            var spotifyAudioFeatures = new List<TrackAudioFeatures>();
            var audioFeatures = new List<AudioFeatures>();
            try
            {
                for (var i = 0; i < trackIds.Count; i += 100)
                {
                    var request = new TracksAudioFeaturesRequest(trackIds.Skip(i).Take(100).ToList());
                    var response = await Spotify.Tracks.GetSeveralAudioFeatures(request);
                    spotifyAudioFeatures.AddRange(response.AudioFeatures);
                }


                // audio features can be null
                // TODO better workaround (the entire UI and FilterNodes are currently designed such that every track has AudioFeatures)
                for (var i = 0; i < spotifyAudioFeatures.Count; i++)
                {
                    var af = spotifyAudioFeatures[i];
                    if (af == null)
                    {
                        audioFeatures.Add(new AudioFeatures
                        {
                            Id = trackIds[i],
                            Acousticness = 0,
                            Danceability = 0,
                            Energy = 0,
                            Instrumentalness = 0,
                            Key = -1,
                            Liveness = 0,
                            Loudness = 0,
                            Mode = 0,
                            Speechiness = 0,
                            Tempo = 0,
                            TimeSignature = 4,
                            Valence = 0,
                        });
                    }
                    else
                    {
                        audioFeatures.Add(new AudioFeatures
                        {
                            Id = af.Id,
                            Acousticness = af.Acousticness,
                            Danceability = af.Danceability,
                            Energy = af.Energy,
                            Instrumentalness = af.Instrumentalness,
                            Key = af.Key,
                            Liveness = af.Liveness,
                            Loudness = af.Loudness,
                            Mode = af.Mode,
                            Speechiness = af.Speechiness,
                            Tempo = af.Tempo,
                            TimeSignature = af.TimeSignature,
                            Valence = af.Valence,
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Error in {nameof(GetAudioFeatures)}: {e.Message}");
            }
            return audioFeatures;
        }


        public static async Task<(List<Playlist>, Dictionary<string, Track>)> GetFullLibrary(List<string> generatedPlaylistIds)
        {
            try
            {
                var likedTracksTask = SpotifyOperations.GetLikedTracks();
                var playlistsTask = SpotifyOperations.GetCurrentUsersPlaylists();
                var playlistsTracksTask = playlistsTask.ContinueWith(playlists =>
                {
                    var playlistTasks = playlists.Result
                        .Where(pl => !generatedPlaylistIds.Contains(pl.Id))
                        .Select(p => SpotifyOperations.GetPlaylistTracks(p.Id)).ToArray();
                    Task.WaitAll(playlistTasks);
                    return playlistTasks.Select(playlistTask => playlistTask.Result).ToList();
                });

                // add liked tracks to local mirror
                var likedTracks = await likedTracksTask;
                var tracks = likedTracks.ToDictionary(t => t.Id, t => t);


                // add tracks from liked playlists
                var playlists = (await playlistsTask).Where(pl => !generatedPlaylistIds.Contains(pl.Id)).ToList();
                var playlistsTracks = await playlistsTracksTask;
                Logger.Information("Start fetching full spotify library");
                for (var i = 0; i < playlists.Count; i++)
                {
                    Logger.Information($"Fetching tracks from playlist \"{playlists[i].Name}\" {i + 1}/{playlists.Count} " +
                        $"({playlistsTracks[i].Count} tracks)");
                    var playlist = playlists[i];
                    foreach (var track in playlistsTracks[i])
                    {
                        if (tracks.TryGetValue(track.Id, out var addedTrack))
                        {
                            // track is multiple times in library --> only add playlist name to track
                            addedTrack.Playlists.Add(playlist);
                            // set IsLiked (should not be needed because likedTracks are added first)
                            addedTrack.IsLiked = addedTrack.IsLiked || track.IsLiked;
                        }
                        else
                        {
                            // track is first encountered in this playlist
                            track.Playlists = new List<Playlist> { playlist };
                            tracks[track.Id] = track;
                        }
                    }
                }
                Logger.Information("Finished fetching full spotify library");
                return (playlists, tracks);
            }
            catch (Exception e)
            {
                Logger.Error($"Error in GetFullLibrary: {e.Message}");
                return (null, null);
            }
        }

        
        public static async Task<Error> SyncPlaylistOutputNode(PlaylistOutputNode playlistOutputNode)
        {
            if (playlistOutputNode.AnyBackward(gn => !gn.IsValid))
            {
                Logger.Information($"cannot run PlaylistOutputNode Id={playlistOutputNode.Id} (encountered invalid graphNode)");
                return SyncPlaylistOutputNodeErrors.ContainsInvalidNode;
            }
            Logger.Information($"synchronizing PlaylistOutputNode {playlistOutputNode.PlaylistName} to spotify");

            if (playlistOutputNode.GeneratedPlaylistId == null)
            {
                // create playlist
                try
                {
                    var request = new PlaylistCreateRequest(playlistOutputNode.PlaylistName)
                    {
                        Description = "Automatically generated playlist by \"Song Tagger for Spotify\" (https://github.com/BenediktAlkin/SpotifySongTagger)"
                    };
                    var createdPlaylist = await Spotify.Playlists.Create(DataContainer.Instance.User.Id, request);

                    if (DatabaseOperations.EditPlaylistOutputNodeGeneratedPlaylistId(playlistOutputNode, createdPlaylist.Id))
                        playlistOutputNode.GeneratedPlaylistId = createdPlaylist.Id;
                }
                catch(Exception e)
                {
                    Logger.Error($"Failed to create playlist{Environment.NewLine}{e.Message}");
                    return SyncPlaylistOutputNodeErrors.FailedCreatePlaylist;
                }
                
            }
            else
            {
                FullPlaylist playlistDetails;
                try
                {
                    playlistDetails = await Spotify.Playlists.Get(playlistOutputNode.GeneratedPlaylistId);
                    // like playlist if it was unliked
                    var followCheckReq = new FollowCheckPlaylistRequest(new List<string> { DataContainer.Instance.User.Id });
                    if (!(await Spotify.Follow.CheckPlaylist(playlistOutputNode.GeneratedPlaylistId, followCheckReq)).First())
                        await Spotify.Follow.FollowPlaylist(playlistOutputNode.GeneratedPlaylistId);
                }
                catch (Exception e)
                {
                    Logger.Error($"Failed to like playlist to generate{Environment.NewLine}{e.Message}");
                    return SyncPlaylistOutputNodeErrors.FailedLike;
                }
                try { 
                    // rename spotify playlist if name changed
                    if (playlistDetails.Name != playlistOutputNode.PlaylistName)
                    {
                        var changeNameReq = new PlaylistChangeDetailsRequest { Name = playlistOutputNode.PlaylistName };
                        await Spotify.Playlists.ChangeDetails(playlistOutputNode.GeneratedPlaylistId, changeNameReq);
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"Failed to rename playlist to generate{Environment.NewLine}{e.Message}");
                    return SyncPlaylistOutputNodeErrors.FailedRename;
                }
                try { 
                    // remove everything
                    if (playlistDetails.Tracks.Total.Value > 0)
                    {
                        var snapshotId = playlistDetails.SnapshotId;
                        for (var i = 0; i < playlistDetails.Tracks.Total.Value; i += 100)
                        {
                            var count = Math.Min(playlistDetails.Tracks.Total.Value - i, 100);
                            var request = new PlaylistRemoveItemsRequest
                            {
                                Positions = Enumerable.Range(i, count).ToList(),
                                SnapshotId = snapshotId,
                            };
                            var response = await Spotify.Playlists.RemoveItems(playlistOutputNode.GeneratedPlaylistId, request);
                            //snapshotId = response.SnapshotId;
                            Logger.Information($"removed {i}-{i + count} from {playlistDetails.Name}");
                        }
                    }
                }
                catch(Exception e)
                {
                    if (e.Message.Contains("Playlist size limit reached"))
                    {
                        Logger.Error($"Playlist size limit reached{Environment.NewLine}{e.Message}");
                        return SyncPlaylistOutputNodeErrors.ReachedSizeLimit;
                    }

                    // SpotifyAPI randomly returns "Internal server error" after like 1000 deletes
                    if (playlistDetails.Tracks.Total.Value > 1000)
                    {
                        Logger.Error($"Failed to remove_everything from playlist to generate (unstable API for sizes > 1000){Environment.NewLine}{e.Message}");
                        return SyncPlaylistOutputNodeErrors.SpotifyAPIRemoveUnstable;
                    }

                    Logger.Error($"Failed to remove_everything from playlist to generate{Environment.NewLine}{e.Message}");
                    return SyncPlaylistOutputNodeErrors.FailedRemoveOldTracks;
                }
            }

            // sync with spotify
            const int BATCH_SIZE = 100;
            playlistOutputNode.CalculateOutputResult();
            var tracks = playlistOutputNode.OutputResult;
            try
            {
                for (var i = 0; i < tracks.Count; i += BATCH_SIZE)
                {
                    var count = Math.Min(tracks.Count - i, BATCH_SIZE);
                    var request = new PlaylistAddItemsRequest(
                        Enumerable.Range(0, count)
                            .Select(j => $"spotify:track:{tracks[i + j].Id}")
                            .ToList());
                    await Spotify.Playlists.AddItems(playlistOutputNode.GeneratedPlaylistId, request);
                    Logger.Information($"added tracks {i}-{i + count} to {playlistOutputNode.PlaylistName}");
                }
            }
            catch(Exception e)
            {
                Logger.Error($"Failed to add track to playlist{Environment.NewLine}{e.Message}");
                return SyncPlaylistOutputNodeErrors.FailedAddNewTrack;
            }
            
            Logger.Information($"synchronized PlaylistOutputNode {playlistOutputNode.PlaylistName} to spotify");
            return null;
        }

        public static async Task<List<FullArtist>> GetArtists(List<string> artistIds)
        {
            const int BATCH_SIZE = 50;
            var artists = new List<FullArtist>();
            for (var i=0;i<artistIds.Count;i += BATCH_SIZE)
            {
                try
                {
                    var request = new ArtistsRequest(artistIds.Skip(i).Take(BATCH_SIZE).ToList());
                    var response = await Spotify.Artists.GetSeveral(request);
                    artists.AddRange(response.Artists);
                }
                catch(Exception e)
                {
                    // exception is thrown when Artist is not found (?)
                    for (var j = 0; j < BATCH_SIZE; j++)
                    {
                        try
                        {
                            var request = new ArtistsRequest(artistIds.Skip(i + j).Take(1).ToList());
                            var response = await Spotify.Artists.GetSeveral(request);
                            artists.AddRange(response.Artists);
                        }
                        catch (Exception ex)
                        {
                            // add null artist such that length is equal to ids
                            artists.Add(null);
                            Logger.Error($"Error in GetArtist (probably couldn't find artist) " +
                                $"ArtistId={artistIds[i+j]} OuterExceptionMessage={e.Message} " +
                                $"ExceptionMessage={ex.Message}");
                        }
                    }
                }
            }
            return artists;
        }
    }
}
