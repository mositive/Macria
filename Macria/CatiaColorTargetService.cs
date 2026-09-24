using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Macria
{
    // Renk hedefinin kapsamını, COM nesnesini ve kimlik kanıtını birbirinden ayırır.
    // Bu model yalnız işlem süresince kullanılır; kalıcı renk geçmişi tutmaz.
    internal enum CatiaColorTargetType
    {
        Occurrence,
        PartBody,
        Product
    }

    internal sealed class CatiaColorTargetIdentity
    {
        public CatiaColorTargetIdentity(
            CatiaColorTargetType targetType,
            string sessionObjectKey,
            string? referenceKey,
            string comType)
        {
            TargetType = targetType;
            SessionObjectKey = sessionObjectKey;
            ReferenceKey = referenceKey;
            ComType = comType;
        }

        public CatiaColorTargetType TargetType { get; }
        // COM IUnknown tabanlı, yalnız açık CATIA oturumu için geçerli hedef kimliği.
        public string SessionObjectKey { get; }
        // Varsa PLM_ExternalID + sürümden gelen referans kimliği; görünen Title değildir.
        public string? ReferenceKey { get; }
        public string ComType { get; }
    }

    internal sealed class CatiaColorTarget
    {
        public CatiaColorTarget(object comObject, CatiaColorTargetIdentity identity)
        {
            ComObject = comObject;
            Identity = identity;
        }

        public object ComObject { get; }
        public CatiaColorTargetIdentity Identity { get; }
    }

    internal readonly struct CatiaColorRgb
    {
        public CatiaColorRgb(int red, int green, int blue, int status)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Status = status;
        }

        public int Red { get; }
        public int Green { get; }
        public int Blue { get; }
        // VisPropertySet.GetRealColor dönüş değeri (CatVisPropertyStatus).
        public int Status { get; }
    }

    internal readonly struct CatiaColorInheritance
    {
        public CatiaColorInheritance(int inheritance, int status)
        {
            Inheritance = inheritance;
            Status = status;
        }

        // 0: kalıtım yok, 1: kalıtım var. Yalnız Status=Defined ise geçerlidir.
        public int Inheritance { get; }
        public int Status { get; }
    }

    internal sealed class CatiaColorTargetResult
    {
        private CatiaColorTargetResult(CatiaColorTarget? target, string error)
        {
            Target = target;
            Error = error;
        }

        public CatiaColorTarget? Target { get; }
        public string Error { get; }
        public bool Success => Target != null;

        public static CatiaColorTargetResult Ok(CatiaColorTarget target) => new(target, "");
        public static CatiaColorTargetResult Fail(string error) => new(null, error);
    }

    internal sealed class CatiaColorOperationResult
    {
        private CatiaColorOperationResult(bool success, CatiaColorRgb? color, string error, Exception? exception)
        {
            Success = success;
            Color = color;
            Error = error;
            Exception = exception;
        }

        public bool Success { get; }
        public CatiaColorRgb? Color { get; }
        public string Error { get; }
        public Exception? Exception { get; }

        public static CatiaColorOperationResult Ok(CatiaColorRgb? color = null) =>
            new(true, color, "", null);

        public static CatiaColorOperationResult Fail(string error, Exception? exception = null) =>
            new(false, null, error, exception);
    }

    internal sealed class CatiaColorInheritanceOperationResult
    {
        private CatiaColorInheritanceOperationResult(
            bool success,
            CatiaColorInheritance? inheritance,
            string error,
            Exception? exception)
        {
            Success = success;
            Inheritance = inheritance;
            Error = error;
            Exception = exception;
        }

        public bool Success { get; }
        public CatiaColorInheritance? Inheritance { get; }
        public string Error { get; }
        public Exception? Exception { get; }

        public static CatiaColorInheritanceOperationResult Ok(CatiaColorInheritance inheritance) =>
            new(true, inheritance, "", null);

        public static CatiaColorInheritanceOperationResult Fail(string error, Exception? exception = null) =>
            new(false, null, error, exception);
    }

    // CATIA COM yalnız çağıranın mevcut UI/COM thread'inde kullanılmalıdır.
    // Selection korunması çağırana aittir; bu sınıf her geçici seçimden sonra Clear yapar.
    internal sealed class CatiaColorTargetService
    {
        // InfTypeLib.tlb / CatVisPropertyType: catVisPropertyColor = 2.
        private const int CatVisPropertyColor = 2;

        private readonly Func<object, object?> _referenceResolver;

        public CatiaColorTargetService(Func<object, object?> referenceResolver)
        {
            _referenceResolver = referenceResolver ?? throw new ArgumentNullException(nameof(referenceResolver));
        }

        public CatiaColorTargetResult ResolveOccurrence(object? selectedObject)
        {
            if (selectedObject == null)
                return CatiaColorTargetResult.Fail("Occurrence hedefi seçilmedi.");

            try
            {
                object? entity = ((dynamic)selectedObject).PLMEntity;
                if (entity == null)
                    return CatiaColorTargetResult.Fail("Seçilen nesnenin PLMEntity ilişkisi yok; occurrence doğrulanamadı.");
            }
            catch (Exception ex)
            {
                return CatiaColorTargetResult.Fail("Seçilen nesnenin PLMEntity ilişkisi okunamadı: " + ex.Message);
            }

            return CreateTarget(CatiaColorTargetType.Occurrence, selectedObject, selectedObject);
        }

        public CatiaColorTargetResult ResolvePartBody(object? activeObject)
        {
            if (activeObject == null)
                return CatiaColorTargetResult.Fail("ActiveEditor.ActiveObject alınamadı; hedef parçayı ayrı parça editöründe açın.");

            object? mainBody = null;
            Exception? firstError = null;
            try { mainBody = ((dynamic)activeObject).MainBody; }
            catch (Exception ex) { firstError = ex; }

            if (mainBody == null)
            {
                try
                {
                    dynamic bodies = ((dynamic)activeObject).Bodies;
                    mainBody = bodies == null ? null : bodies.MainBody;
                }
                catch (Exception ex)
                {
                    firstError ??= ex;
                }
            }

            if (mainBody == null)
            {
                string type = ComProbe.TipAdi(activeObject);
                string detail = firstError == null ? "" : " " + firstError.Message;
                return CatiaColorTargetResult.Fail(
                    "ActiveObject gerçek bir PartBody sunmuyor (COM türü=" + type + "). " +
                    "Hedef parçayı ayrı parça editöründe açın." + detail);
            }

            return CreateTarget(CatiaColorTargetType.PartBody, mainBody, activeObject);
        }

        public CatiaColorTargetResult ResolveProduct(object? selectedObject)
        {
            if (selectedObject == null)
                return CatiaColorTargetResult.Fail("Product hedefi seçilmedi.");

            try
            {
                string type = ComProbe.TipAdi(selectedObject);
                if (type.IndexOf("Occurrence", StringComparison.OrdinalIgnoreCase) < 0)
                    return CatiaColorTargetResult.Fail("Seçilen nesne Product occurrence değildir (COM türü=" + type + ").");

                dynamic occurrences = ((dynamic)selectedObject).Occurrences;
                int childCount = occurrences == null ? 0 : Convert.ToInt32(occurrences.Count);
                if (childCount <= 0)
                    return CatiaColorTargetResult.Fail("Seçilen occurrence yapraktır; Product testi için alt occurrence içeren bir grup seçin.");

                object? reference = _referenceResolver(selectedObject);
                if (reference == null)
                    return CatiaColorTargetResult.Fail("Seçilen Product occurrence için PLM referansı çözümlenemedi.");

                return CreateTarget(CatiaColorTargetType.Product, selectedObject, selectedObject, reference);
            }
            catch (Exception ex)
            {
                return CatiaColorTargetResult.Fail("Product hedefi doğrulanamadı: " + ex.Message);
            }
        }

        public CatiaColorOperationResult TryReadRgb(dynamic selection, CatiaColorTarget target)
        {
            if (selection == null)
                return CatiaColorOperationResult.Fail("CATIA Selection alınamadı.");
            if (target == null)
                return CatiaColorOperationResult.Fail("Renk hedefi alınamadı.");

            try
            {
                selection.Clear();
                selection.Add(target.ComObject);
                dynamic properties = selection.VisProperties;
                int red = 0;
                int green = 0;
                int blue = 0;

                // CATIA VisPropertySet: üç ref RGB parametresi, dönüşte CatVisPropertyStatus.
                object? statusObject = properties.GetRealColor(ref red, ref green, ref blue);
                int status = Convert.ToInt32(statusObject);
                return CatiaColorOperationResult.Ok(new CatiaColorRgb(red, green, blue, status));
            }
            catch (Exception ex)
            {
                return CatiaColorOperationResult.Fail("GetRealColor çağrısı başarısız: " + ex.Message, ex);
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        public CatiaColorOperationResult TryReadVisibleRgb(dynamic selection, CatiaColorTarget target)
        {
            if (selection == null)
                return CatiaColorOperationResult.Fail("CATIA Selection alınamadı.");
            if (target == null)
                return CatiaColorOperationResult.Fail("Renk hedefi alınamadı.");

            try
            {
                selection.Clear();
                selection.Add(target.ComObject);
                dynamic properties = selection.VisProperties;
                int red = 0;
                int green = 0;
                int blue = 0;

                // VisPropertySet.GetVisibleColor(out long, out long, out long) -> CatVisPropertyStatus.
                object? statusObject = properties.GetVisibleColor(ref red, ref green, ref blue);
                int status = Convert.ToInt32(statusObject);
                return CatiaColorOperationResult.Ok(new CatiaColorRgb(red, green, blue, status));
            }
            catch (Exception ex)
            {
                return CatiaColorOperationResult.Fail("GetVisibleColor çağrısı başarısız: " + ex.Message, ex);
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        public CatiaColorInheritanceOperationResult TryReadRealColorInheritance(
            dynamic selection,
            CatiaColorTarget target)
        {
            if (selection == null)
                return CatiaColorInheritanceOperationResult.Fail("CATIA Selection alınamadı.");
            if (target == null)
                return CatiaColorInheritanceOperationResult.Fail("Renk hedefi alınamadı.");

            try
            {
                selection.Clear();
                selection.Add(target.ComObject);
                dynamic properties = selection.VisProperties;
                int inheritance = 0;

                // GetRealInheritance(CatVisPropertyType, out long) -> CatVisPropertyStatus.
                object? statusObject = properties.GetRealInheritance(CatVisPropertyColor, ref inheritance);
                int status = Convert.ToInt32(statusObject);
                return CatiaColorInheritanceOperationResult.Ok(
                    new CatiaColorInheritance(inheritance, status));
            }
            catch (Exception ex)
            {
                return CatiaColorInheritanceOperationResult.Fail(
                    "GetRealInheritance(catVisPropertyColor) çağrısı başarısız: " + ex.Message,
                    ex);
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        public CatiaColorInheritanceOperationResult TryReadVisibleColorInheritance(
            dynamic selection,
            CatiaColorTarget target)
        {
            if (selection == null)
                return CatiaColorInheritanceOperationResult.Fail("CATIA Selection alınamadı.");
            if (target == null)
                return CatiaColorInheritanceOperationResult.Fail("Renk hedefi alınamadı.");

            try
            {
                selection.Clear();
                selection.Add(target.ComObject);
                dynamic properties = selection.VisProperties;
                int inheritance = 0;

                // GetVisibleInheritance(CatVisPropertyType, out long) -> CatVisPropertyStatus.
                object? statusObject = properties.GetVisibleInheritance(CatVisPropertyColor, ref inheritance);
                int status = Convert.ToInt32(statusObject);
                return CatiaColorInheritanceOperationResult.Ok(
                    new CatiaColorInheritance(inheritance, status));
            }
            catch (Exception ex)
            {
                return CatiaColorInheritanceOperationResult.Fail(
                    "GetVisibleInheritance(catVisPropertyColor) çağrısı başarısız: " + ex.Message,
                    ex);
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        public CatiaColorOperationResult TryApplyRgb(dynamic selection, CatiaColorTarget target, int red, int green, int blue)
        {
            if (selection == null)
                return CatiaColorOperationResult.Fail("CATIA Selection alınamadı.");
            if (target == null)
                return CatiaColorOperationResult.Fail("Renk hedefi alınamadı.");

            try
            {
                selection.Clear();
                selection.Add(target.ComObject);
                dynamic properties = selection.VisProperties;
                properties.SetRealColor(red, green, blue, 1);
                return CatiaColorOperationResult.Ok();
            }
            catch (Exception ex)
            {
                return CatiaColorOperationResult.Fail("SetRealColor çağrısı başarısız: " + ex.Message, ex);
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        public CatiaColorOperationResult TryResetColor(dynamic selection, CatiaColorTarget target)
        {
            if (selection == null)
                return CatiaColorOperationResult.Fail("CATIA Selection alınamadı.");
            if (target == null)
                return CatiaColorOperationResult.Fail("Renk hedefi alınamadı.");

            try
            {
                selection.Clear();
                selection.Add(target.ComObject);
                dynamic properties = selection.VisProperties;
                // DSYAutomation.chm / InfTypeLib.tlb: ResetProperty(CatVisPropertyType).
                properties.ResetProperty(CatVisPropertyColor);
                return CatiaColorOperationResult.Ok();
            }
            catch (Exception ex)
            {
                return CatiaColorOperationResult.Fail(
                    "ResetProperty(catVisPropertyColor) çağrısı başarısız: " + ex.Message,
                    ex);
            }
            finally
            {
                try { selection.Clear(); } catch { }
            }
        }

        // Yalnız açık CATIA oturumunda geçerli COM kimliği. Kalıcı nesne kimliği değildir.
        internal static string GetSessionObjectKey(object sessionObject)
        {
            if (sessionObject == null) throw new ArgumentNullException(nameof(sessionObject));
            return SessionObjectKey(sessionObject, ComProbe.TipAdi(sessionObject));
        }

        private CatiaColorTargetResult CreateTarget(
            CatiaColorTargetType targetType,
            object targetObject,
            object referenceSource,
            object? knownReference = null)
        {
            try
            {
                object? reference = knownReference ?? _referenceResolver(referenceSource);
                string? referenceKey = ReferenceKey(reference);
                string comType = ComProbe.TipAdi(targetObject);
                string sessionKey = SessionObjectKey(targetObject, comType);
                var identity = new CatiaColorTargetIdentity(targetType, sessionKey, referenceKey, comType);
                return CatiaColorTargetResult.Ok(new CatiaColorTarget(targetObject, identity));
            }
            catch (Exception ex)
            {
                return CatiaColorTargetResult.Fail("Renk hedefi kimliği oluşturulamadı: " + ex.Message);
            }
        }

        private static string? ReferenceKey(object? reference)
        {
            if (reference == null) return null;

            string externalId = PlmValue(reference, "PLM_ExternalID");
            string version = PlmValue(reference, "V_version");
            if (string.IsNullOrWhiteSpace(version))
                version = PlmValue(reference, "revision");

            return AkilliRenklendirmeMantigi.ReferansAnahtari(externalId, version);
        }

        private static string PlmValue(object reference, string member)
        {
            try
            {
                dynamic valueSource = reference;
                object? value = member switch
                {
                    "PLM_ExternalID" => valueSource.PLM_ExternalID,
                    "V_version" => valueSource.V_version,
                    "revision" => valueSource.revision,
                    _ => null
                };
                return Convert.ToString(value)?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string SessionObjectKey(object targetObject, string comType)
        {
            IntPtr unknown = IntPtr.Zero;
            try
            {
                if (Marshal.IsComObject(targetObject))
                {
                    unknown = Marshal.GetIUnknownForObject(targetObject);
                    return "COM:" + comType + ":" + unknown.ToInt64().ToString("X");
                }
            }
            catch
            {
                // Yalnız tanı kimliği için güvenli RCW fallback'i kullanılır.
            }
            finally
            {
                if (unknown != IntPtr.Zero)
                    Marshal.Release(unknown);
            }

            return "RCW:" + comType + ":" + RuntimeHelpers.GetHashCode(targetObject);
        }
    }
}
