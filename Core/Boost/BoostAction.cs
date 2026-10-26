using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GBooster.Core.Boost
{
    /// <summary>
    /// Guarda, em actions.json, o que cada ajuste mudou fora do registro (plano de energia, bcdedit, fsutil…),
    /// para o "Desfazer" devolver exatamente o que havia antes.
    /// </summary>
    public static class ActionStore
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
        private static readonly object Gate = new object();

        private static string PathFile => System.IO.Path.Combine(Store.Folder, "actions.json");

        private static Dictionary<string, Dictionary<string, string>> Load()
        {
            try
            {
                if (File.Exists(PathFile))
                {
                    var d = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(PathFile));
                    if (d != null) return d;
                }
            }
            catch
            {
                // arquivo corrompido: começa vazio
            }
            return new Dictionary<string, Dictionary<string, string>>();
        }

        private static void Save(Dictionary<string, Dictionary<string, string>> d)
        {
            try { File.WriteAllText(PathFile, JsonSerializer.Serialize(d, Json)); }
            catch { /* sem backup extra: o ajuste ainda funciona, só não desfaz esta parte */ }
        }

        public static bool Has(string id)
        {
            lock (Gate) { return Load().ContainsKey(id); }
        }

        public static Dictionary<string, string> Get(string id)
        {
            lock (Gate)
            {
                Dictionary<string, Dictionary<string, string>> all = Load();
                return all.TryGetValue(id, out Dictionary<string, string>? v) ? new Dictionary<string, string>(v) : new Dictionary<string, string>();
            }
        }

        public static void Set(string id, Dictionary<string, string> values)
        {
            lock (Gate)
            {
                Dictionary<string, Dictionary<string, string>> all = Load();
                all[id] = values;
                Save(all);
            }
        }

        public static void Remove(string id)
        {
            lock (Gate)
            {
                Dictionary<string, Dictionary<string, string>> all = Load();
                if (all.Remove(id)) Save(all);
            }
        }
    }

    /// <summary>
    /// Um ajuste do Otimizar. Pode ter uma parte de registro (<see cref="Reg"/>, com backup automático)
    /// e uma parte "extra" (comandos do Windows). Aplicar e desfazer são seguros para repetir.
    /// </summary>
    public abstract class BoostAction
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public bool NeedsReboot { get; protected set; }
        /// <summary>false = não há como desfazer (ex.: apps removidos).</summary>
        public bool Reversible { get; protected set; } = true;
        /// <summary>Peso na barra de progresso (ajustes lentos pesam mais).</summary>
        public int Weight { get; protected set; } = 1;
        protected Tweak? Reg { get; set; }
        /// <summary>true quando o ajuste tem a parte extra (comandos do Windows) além do registro.</summary>
        protected virtual bool HasExtra => false;

        protected BoostAction(string id, string title, string description)
        {
            Id = id; Title = title; Description = description;
        }

        public virtual bool IsApplicable() => true;

        /// <summary>Motivo mostrado quando o ajuste não vale para este PC.</summary>
        public virtual string NotApplicableText => Loc.T("Não se aplica a este PC.");

        /// <summary>null = este ajuste não tem parte extra.</summary>
        protected virtual bool? ExtraApplied() => null;

        /// <summary>Aplica a parte extra. <paramref name="bk"/> guarda os valores de antes. Devolve null se deu certo, ou o erro.</summary>
        protected virtual string? ExtraApply(Dictionary<string, string> bk) => null;

        /// <summary>Desfaz a parte extra. Devolve null se deu certo, ou o erro.</summary>
        protected virtual string? ExtraUndo(Dictionary<string, string> bk) => null;

        public bool HasBackup() => ActionStore.Has(Id) || (Reg != null && TweakService.HasBackup(Reg));

        public TweakState GetState()
        {
            if (!IsApplicable()) return TweakState.NotApplicable;
            bool? extra;
            try { extra = ExtraApplied(); }
            catch { extra = false; }

            if (Reg == null)
            {
                return extra == true ? TweakState.Applied : TweakState.NotApplied;
            }

            TweakState rs = TweakService.GetState(Reg);
            if (rs == TweakState.Applied && extra != false) return TweakState.Applied;
            if (rs == TweakState.NotApplied && extra != true) return TweakState.NotApplied;
            return TweakState.Partial;
        }

        public TweakResult Apply(bool force = false)
        {
            var r = new TweakResult();
            if (!IsApplicable())
            {
                r.Ok = true; r.NoChange = true; r.Skipped = true;
                r.Message = NotApplicableText;
                return r;
            }
            if (!AdminHelper.IsAdmin)
            {
                r.Message = Loc.T("Precisa do modo administrador.");
                return r;
            }
            if (!force && GetState() == TweakState.Applied)
            {
                r.Ok = true; r.NoChange = true;
                r.Message = Loc.T("Já estava aplicado.");
                return r;
            }

            var errors = new List<string>();
            bool changed = false;

            if (HasExtra)
            {
                Dictionary<string, string> bk = ActionStore.Get(Id);
                try
                {
                    string? err = ExtraApply(bk);
                    ActionStore.Set(Id, bk);
                    if (err != null) errors.Add(err);
                    else changed = true;
                }
                catch (Exception ex)
                {
                    ActionStore.Set(Id, bk);
                    errors.Add(ex.Message);
                }
            }

            if (Reg != null)
            {
                TweakResult rr = TweakService.Apply(Reg);
                if (!rr.Ok) errors.Add(rr.Message);
                else if (!rr.NoChange) changed = true;
            }

            if (errors.Count > 0)
            {
                r.Message = string.Join(" | ", errors);
                return r;
            }
            r.Ok = true;
            r.NoChange = !changed;
            r.NeedsReboot = NeedsReboot;
            r.Message = Loc.T("Aplicado.");
            return r;
        }

        public TweakResult Undo()
        {
            var r = new TweakResult();
            if (!Reversible)
            {
                r.Ok = true; r.NoChange = true;
                r.Message = Loc.T("Não volta pelo Desfazer.");
                return r;
            }
            if (!HasBackup())
            {
                r.Ok = true; r.NoChange = true;
                r.Message = Loc.T("Nada para desfazer: o app não guardou valores anteriores.");
                return r;
            }
            if (!AdminHelper.IsAdmin)
            {
                r.Message = Loc.T("Precisa do modo administrador.");
                return r;
            }

            var errors = new List<string>();
            if (HasExtra && ActionStore.Has(Id))
            {
                Dictionary<string, string> bk = ActionStore.Get(Id);
                try
                {
                    string? err = ExtraUndo(bk);
                    if (err != null) errors.Add(err);
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }
            if (Reg != null)
            {
                TweakResult rr = TweakService.Undo(Reg);
                if (!rr.Ok) errors.Add(rr.Message);
            }
            if (errors.Count > 0)
            {
                r.Message = string.Join(" | ", errors);
                return r;
            }

            ActionStore.Remove(Id);
            r.Ok = true;
            r.NeedsReboot = NeedsReboot;
            r.Message = Loc.T("Desfeito.");
            return r;
        }
    }
}
