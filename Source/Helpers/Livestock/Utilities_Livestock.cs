﻿// Karel Kroeze
// Utilities_Livestock.cs
// 2016-12-09

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FluffyManager
{
    public enum AgeAndSex
    {
        AdultFemale = 0,
        AdultMale = 1,
        JuvenileFemale = 2,
        JuvenileMale = 3
    }

    [Flags]
    public enum MasterMode
    {
        Default = 0,
        Hunters = 1,
        Trainers = 2,
        Melee = 4,
        Ranged = 8,
        Violent = 16,
        NonViolent = 32,
        All = Hunters | Trainers | Melee | Ranged | Violent | NonViolent,
        Specific = 64 
    }

    public static class Utilities_Livestock
    {

        public static AgeAndSex[] AgeSexArray = (AgeAndSex[]) Enum.GetValues( typeof( AgeAndSex ) );

        private static Dictionary<Pair<PawnKindDef, Map>, Utilities.CachedValue<IEnumerable<Pawn>>> _allCache =
            new Dictionary<Pair<PawnKindDef, Map>, Utilities.CachedValue<IEnumerable<Pawn>>>();

        private static Dictionary<Triplet<PawnKindDef, Map, MasterMode>, Utilities.CachedValue<IEnumerable<Pawn>>> _masterCache =
                new Dictionary<Triplet<PawnKindDef, Map, MasterMode>, Utilities.CachedValue<IEnumerable<Pawn>>>();

        private static Dictionary<Pawn, Utilities.CachedValue<IEnumerable<Pawn>>> _followerCache =
            new Dictionary<Pawn, Utilities.CachedValue<IEnumerable<Pawn>>>();

        private static Dictionary<PawnKindDef, Utilities.CachedValue<bool>> _milkablePawnkind =
            new Dictionary<PawnKindDef, Utilities.CachedValue<bool>>();

        private static Dictionary<Pawn, Utilities.CachedValue<bool>> _milkablePawn =
            new Dictionary<Pawn, Utilities.CachedValue<bool>>();

        private static Dictionary<PawnKindDef, Utilities.CachedValue<bool>> _shearablePawnkind =
            new Dictionary<PawnKindDef, Utilities.CachedValue<bool>>();

        private static Dictionary<Pawn, Utilities.CachedValue<bool>> _shearablePawn =
            new Dictionary<Pawn, Utilities.CachedValue<bool>>();



        public static bool Juvenile( this AgeAndSex ageSex )
        {
            return ageSex == AgeAndSex.JuvenileFemale || ageSex == AgeAndSex.JuvenileMale;
        }

        public static bool PawnIsOfAgeSex( this Pawn p, AgeAndSex ageSex )
        {
            // we're making the assumption here that anything with a lifestage index of 2 or greater is adult - so 3 lifestages.
            // this works for vanilla and all modded animals that I know off.

            switch ( ageSex )
            {
                case AgeAndSex.AdultFemale:
                    return p.gender == Gender.Female && p.ageTracker.CurLifeStageIndex >= 2;

                case AgeAndSex.AdultMale:
                    return p.gender == Gender.Male && p.ageTracker.CurLifeStageIndex >= 2;

                case AgeAndSex.JuvenileFemale:
                    return p.gender == Gender.Female && p.ageTracker.CurLifeStageIndex < 2;

                case AgeAndSex.JuvenileMale:
                default:
                    return p.gender == Gender.Male && p.ageTracker.CurLifeStageIndex < 2;
            }
        }

        public static MasterMode GetMasterMode( this Pawn pawn )
        {
            var mode = MasterMode.Default;
            
            if ( pawn.workSettings.WorkIsActive( WorkTypeDefOf.Hunting ) )
                mode = mode | MasterMode.Hunters;

            if ( pawn.workSettings.WorkIsActive( WorkTypeDefOf.Handling ) )
                mode = mode | MasterMode.Trainers;

            if ( pawn.equipment.Primary?.def.IsMeleeWeapon ?? true ) // no weapon = melee 
                mode = mode | MasterMode.Melee;

            if ( pawn.equipment.Primary?.def.IsRangedWeapon ?? false )
                mode = mode | MasterMode.Ranged;

            if ( !pawn.story.WorkTagIsDisabled( WorkTags.Violent ) )
                mode = mode | MasterMode.Violent;

            else
                mode = mode | MasterMode.NonViolent;

            return mode;
        }

        public static IEnumerable<Pawn> GetAll( this PawnKindDef pawnKind, Map map )
        {
            // check if we have a cached version
            IEnumerable<Pawn> cached;

            // does it exist at all?
            var key = new Pair<PawnKindDef, Map>( pawnKind, map );
            bool cacheExists = _allCache.ContainsKey( key );

            // is it up to date?
            if ( cacheExists &&
                 _allCache[key].TryGetValue( out cached ) && cached != null )
                return cached;

            // if not, get a new list.
            cached = map.mapPawns.AllPawns
                        .Where( p => p.RaceProps.Animal // is animal
                                     && !p.Dead // is alive
                                     && p.kindDef == pawnKind ); // is our managed pawnkind

            // update if key exists
            if ( cacheExists )
                _allCache[key].Update( cached );

            // else add it
            else
            {
                // severely limit cache to only apply for one cycle (one job)
                _allCache.Add( key, new Utilities.CachedValue<IEnumerable<Pawn>>( cached, 2 ) );
            }
            return cached;
        }

        public static List<Pawn> GetMasterOptions( this PawnKindDef pawnkind, Map map, MasterMode mode )
        {
            // check if we have a cached version
            IEnumerable<Pawn> cached;

            // does it exist at all?
            var key = new Triplet<PawnKindDef, Map, MasterMode>( pawnkind, map, mode );
            bool cacheExists = _masterCache.ContainsKey(key);

            // is it up to date?
            if (cacheExists &&
                _masterCache[key].TryGetValue(out cached) && cached != null)
                return cached.ToList();

            // if not, get a new list.
            cached = map.mapPawns.FreeColonistsSpawned
                .Where( p => !p.Dead &&

                    // matches mode
                    ( p.GetMasterMode() & mode ) != MasterMode.Default
                );
            
            // update if key exists
            if (cacheExists)
                _masterCache[key].Update(cached);

            // else add it
            else
            {
                // severely limit cache to only apply for one cycle (one job)
                _masterCache.Add(key, new Utilities.CachedValue<IEnumerable<Pawn>>(cached, 2));
            }
            return cached.ToList();
        }

        public static List<Pawn> GetFollowers( this Pawn pawn )
        {
            // check if we have a cached version
            IEnumerable<Pawn> cached;

            // does it exist at all?
            bool cacheExists = _followerCache.ContainsKey(pawn);

            // is it up to date?
            if (cacheExists && _followerCache[pawn].TryGetValue(out cached) && cached != null)
                return cached.ToList();

            // if not, get a new list.
            cached = pawn.MapHeld.mapPawns.PawnsInFaction( pawn.Faction )
                .Where(p => !p.Dead &&
                            p.RaceProps.Animal &&
                            p.playerSettings.Master == pawn
                );

            // update if key exists
            if (cacheExists)
                _followerCache[pawn].Update(cached);

            // else add it
            else
            {
                // severely limit cache to only apply for one cycle (one job)
                _followerCache.Add(pawn, new Utilities.CachedValue<IEnumerable<Pawn>>(cached, 2));
            }
            return cached.ToList();
        }

        public static List<Pawn> GetFollowers( this Pawn pawn, PawnKindDef pawnKind )
        {
            return GetFollowers( pawn ).Where( f => f.kindDef == pawnKind ).ToList();
        }

        public static List<Pawn> GetTrainers( this PawnKindDef pawnkind, Map map, MasterMode mode )
        {
            return pawnkind.GetMasterOptions( map, mode ).Where( p =>
                        // skill high enough to handle (copied from StatWorker_MinimumHandlingSkill)
                        // NOTE: This does NOT apply postprocessing, so scenario and other offsets DO NOT apply.
                        // we can't actually use StatRequests because they're hardcoded for either Things or BuildableDefs.
                        p.skills.GetSkill( SkillDefOf.Animals ).Level >=
                            Mathf.Clamp(GenMath.LerpDouble(0.3f, 1f, 0f, 9f, pawnkind.RaceProps.wildness ), 0f, 20f) )
                        .ToList();
        }

        public static List<Pawn> GetWild( this PawnKindDef pawnKind, Map map )
        {
            return pawnKind.GetAll( map ).Where( p => p.Faction == null ).ToList();
        }

        public static List<Pawn> GetTame( this PawnKindDef pawnKind, Map map )
        {
            return pawnKind.GetAll( map ).Where( p => p.Faction == Faction.OfPlayer ).ToList();
        }
        
        public static IEnumerable<Pawn> GetAll( this PawnKindDef pawnKind, Map map, AgeAndSex ageSex )
        {
            return pawnKind.GetAll( map ).Where( p => PawnIsOfAgeSex( p, ageSex ) ); // is of age and sex we want
        }

        public static List<Pawn> GetWild( this PawnKindDef pawnKind, Map map, AgeAndSex ageSex )
        {
#if DEBUG_LIFESTOCK_COUNTS
            foreach (Pawn p in GetAll( ageSex )) Log.Message(p.Faction?.GetCallLabel() ?? "NULL" );
            List<Pawn> wild = GetAll( ageSex ).Where( p => p.Faction == null ).ToList();
            Log.Message( "Wildcount " + ageSex + ": " + wild.Count );
            return wild;
#else
            return pawnKind.GetAll( map, ageSex ).Where( p => p.Faction == null ).ToList();
#endif
        }

        public static List<Pawn> GetTame( this PawnKindDef pawnKind, Map map, AgeAndSex ageSex )
        {
#if DEBUG_LIFESTOCK_COUNTS
            List<Pawn> tame = GetAll( ageSex ).Where( p => p.Faction == Faction.OfPlayer ).ToList();
            Log.Message( "Tamecount " + ageSex + ": " + tame.Count );
            return tame;
#else
            return pawnKind.GetAll( map, ageSex ).Where( p => p.Faction == Faction.OfPlayer ).ToList();
#endif
        }

        public static bool Milkable( this PawnKindDef pawnKind )
        {
            if ( pawnKind == null )
                return false;

            var ret = false;
            if ( _milkablePawnkind.ContainsKey( pawnKind ) )
            {
                if ( _milkablePawnkind[pawnKind].TryGetValue( out ret ) )
                {
                    return ret;
                }

                ret = pawnKind.race.comps.OfType<CompProperties_Milkable>().Any( cp => cp.milkDef != null );
                _milkablePawnkind[pawnKind].Update( ret );
                return ret;
            }

            ret = pawnKind.race.comps.OfType<CompProperties_Milkable>().Any( cp => cp.milkDef != null );
            _milkablePawnkind.Add( pawnKind, new Utilities.CachedValue<bool>( ret, Int32.MaxValue ) );
            return ret;
        }

        public static bool VisiblyPregnant( this Pawn pawn )
        {
            return pawn?.health.hediffSet.GetHediffs<Hediff_Pregnant>().Any( hp => hp.Visible ) ?? false;
        }

        public static bool BondedWithColonist( this Pawn pawn )
        {
            return pawn?.relations?.GetFirstDirectRelationPawn( PawnRelationDefOf.Bond, p => p.IsColonist ) != null;
        }

        public static bool Milkable( this Pawn pawn )
        {
            var ret = false;
            if ( _milkablePawn.ContainsKey( pawn ) )
            {
                if ( _milkablePawn[pawn].TryGetValue( out ret ) )
                {
                    return ret;
                }

                ret = pawn._milkable();
                _milkablePawn[pawn].Update( ret );
                return ret;
            }

            ret = pawn._milkable();
            _milkablePawn.Add( pawn, new Utilities.CachedValue<bool>( ret, 5000 ) );
            return ret;
        }

        private static bool _milkable( this Pawn pawn )
        {
            var comp = pawn?.TryGetComp<CompMilkable>();
            object active = false;
            if ( comp != null )
            {
                active = comp.GetPrivatePropertyValue( "Active" );
            }
            return (bool)active;
        }

        public static bool Shearable( this PawnKindDef pawnKind )
        {
            if ( pawnKind == null )
                return false;

            var ret = false;
            if ( _shearablePawnkind.ContainsKey( pawnKind ) )
            {
                if ( _shearablePawnkind[pawnKind].TryGetValue( out ret ) )
                {
                    return ret;
                }

                ret = pawnKind.race.comps.OfType<CompProperties_Shearable>().Any( cp => cp.woolDef != null );
                _shearablePawnkind[pawnKind].Update( ret );
                return ret;
            }

            ret = pawnKind.race.comps.OfType<CompProperties_Shearable>().Any( cp => cp.woolDef != null );
            _shearablePawnkind.Add( pawnKind, new Utilities.CachedValue<bool>( ret, Int32.MaxValue ) );
            return ret;
        }

        public static bool Shearable( this Pawn pawn )
        {
            var ret = false;
            if ( _shearablePawn.ContainsKey( pawn ) )
            {
                if ( _shearablePawn[pawn].TryGetValue( out ret ) )
                {
                    return ret;
                }

                ret = pawn._shearable();
                _shearablePawn[pawn].Update( ret );
                return ret;
            }

            ret = pawn._shearable();
            _shearablePawn.Add( pawn, new Utilities.CachedValue<bool>( ret, 5000 ) );
            return ret;
        }

        private static bool _shearable( this Pawn pawn )
        {
            var comp = pawn?.TryGetComp<CompShearable>();
            object active = false;
            if ( comp != null )
            {
                active = comp.GetPrivatePropertyValue( "Active" );
            }
            return (bool)active;
        }
    }
}
