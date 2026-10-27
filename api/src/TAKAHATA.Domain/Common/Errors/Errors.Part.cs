using ErrorOr;

namespace TAKAHATA.Domain.Common.Errors
{
    public partial class Errors
    {
        public static class Part
        {
            public static Error DuplicatePartCode =>
                Error.Validation(code: "Part.DuplicatePartCode", description: "PartCode đã tồn tại.");

            public static Error CreateFalse =>
                Error.Failure(code: "Part.CreateFalse", description: "Tạo mới part không thành công.");

            public static Error UpdateFalse =>
                Error.Failure(code: "Part.UpdateFalse", description: "Cập nhật part không thành công.");

            public static Error IsClosed =>
                Error.Validation(code: "Part.IsClosed", description: "Part đã đóng, không thể thao tác.");

            public static Error ProcessCutoverLocked =>
                Error.Conflict(
                    code: "PartProcess.CutoverLocked",
                    description: "Công đoạn đã được chốt sang ngày kế tiếp nên không thể chỉnh sửa hoặc xóa.");

            public static Error ApproveFalse =>
                Error.Failure(code: "Part.ApproveFalse", description: "Phê duyệt part không thành công.");

            public static Error DeleteFalse =>
                Error.Failure(code: "Part.DeleteFalse", description: "Xóa part không thành công.");

            public static Error NotFound =>
                Error.NotFound(code: "Part.NotFoundIndex", description: "Không tìm thấy part.");

            public static Error NotFoundProcess =>
                Error.NotFound(code: "PartProcess.NotFoundProcess", description: "Không tìm thấy part process.");

            public static Error UpdatePartProcessFalse =>
                Error.Failure(code: "UpdatePartProcessFalse.UpdateFalse", description: "Cập nhật part process không thành công.");

            public static Error StartPartProcessFalse =>
                Error.Failure(code: "PartProcessFalse.StartFalse", description: "Bắt đầu gia công không thành công.");

            public static Error CompletePartProcessFalse =>
                Error.Failure(code: "PartProcessFalse.CompleteFalse", description: "Bắt đầu gia công không thành công.");

            public static Error PausePartProcessFalse =>
                Error.Failure(code: "PartProcessFalse.PauseFalse", description: "Tạm dừng gia công không thành công.");

            public static Error IncorrectQuantity =>
                Error.Failure(code: "PartProcessFalse.IncorrectQuantity", description: "Số lượng không được lớn hơn số lượng đã lên kế hoạch.");
        }
    }

}
