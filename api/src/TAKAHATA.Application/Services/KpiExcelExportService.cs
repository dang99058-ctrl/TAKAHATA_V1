using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using TAKAHATA.Application.Common.Interface.Services;
using TAKAHATA.Contracts.Report;
using TAKAHATA.Domain.MasterData;

namespace TAKAHATA.Application.Services
{
    public sealed class KpiExcelExportService : IKpiExcelExportService
    {
        private const int HeaderRow = 4;

        public byte[] ExportMachines(
            IReadOnlyCollection<MachineKPIResponse> items,
            DateOnly from,
            DateOnly to)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Machine KPI");
            const int lastColumn = 12;

            CreateTitle(
                worksheet,
                "BÁO CÁO KPI HIỆU SUẤT MÁY",
                from,
                to,
                lastColumn);

            var headers = new[]
            {
            "STT",
            "Ngày",
            "Mã máy",
            "Tên máy",
            "Loại máy",
            "Hoạt động (giờ)",
            "Chờ (giờ)",
            "Gá đặt (giờ)",
            "Bảo trì (giờ)",
            "Lost Time (giờ)",
            "Trạng thái",
            "Hiệu suất (%)"
        };

            WriteHeaders(worksheet, headers);

            var row = HeaderRow + 1;
            var index = 1;

            foreach (var item in items)
            {
                worksheet.Cell(row, 1).Value = index++;
                worksheet.Cell(row, 2).Value = item.ReportDate.ToDateTime(TimeOnly.MinValue);
                worksheet.Cell(row, 3).Value = item.Code ?? string.Empty;
                worksheet.Cell(row, 4).Value = item.Name ?? item.Code ?? string.Empty;
                worksheet.Cell(row, 5).Value = item.Type ?? string.Empty;
                worksheet.Cell(row, 6).Value = item.WorkingTime ?? 0;
                worksheet.Cell(row, 7).Value = item.WaitingTime ?? 0;
                worksheet.Cell(row, 8).Value = item.SetupTime ?? 0;
                worksheet.Cell(row, 9).Value = item.MaintenanceTime ?? 0;
                worksheet.Cell(row, 10).Value = item.LostTime;
                worksheet.Cell(row, 11).Value = item.MachineStatus.ToString();
                worksheet.Cell(row, 12).Value = item.UtilizationRate;
                row++;
            }

            if (row > HeaderRow + 1)
            {
                worksheet.Range(HeaderRow + 1, 2, row - 1, 2)
                    .Style.DateFormat.Format = "dd/MM/yyyy";
                worksheet.Range(HeaderRow + 1, 6, row - 1, 10)
                    .Style.NumberFormat.Format = "0.00";
                worksheet.Range(HeaderRow + 1, 12, row - 1, 12)
                    .Style.NumberFormat.Format = "0.00";
            }

            FinishWorksheet(worksheet, row - 1, lastColumn);
            return Save(workbook);
        }

        private static string GetPartTypeLabel(
            TAKAHATA.Domain.Common.Enum.PartType partType) =>
            partType switch
            {
                TAKAHATA.Domain.Common.Enum.PartType.NewMold => "New Mold",
                TAKAHATA.Domain.Common.Enum.PartType.NewMoldRepairMakeNew => "New Mold Repair",
                TAKAHATA.Domain.Common.Enum.PartType.MoldEcn => "Mold ECN",
                TAKAHATA.Domain.Common.Enum.PartType.MoldMp => "Mold MP",
                TAKAHATA.Domain.Common.Enum.PartType.MoldOvershort => "Mold Overshort",
                TAKAHATA.Domain.Common.Enum.PartType.JigMoldAssyEng =>
                    "Jig / Mold / Assembly / Engineering",
                _ => partType.ToString()
            };

        public async Task<byte[]> ExportPartProcesses(
            IWebHostEnvironment environment,
            IReadOnlyCollection<PartProcessKPIResponse> items,
            DateOnly from,
            DateOnly to,
            List<Machine> machines)
        {
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(items);

            if (from > to)
            {
                throw new ArgumentException(
                    "Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc.",
                    nameof(from));
            }

            const int firstDataRow = 8;
            const int lastColumn = 19; // S

            if (items.Count > 1_048_576 - firstDataRow + 1)
            {
                throw new ArgumentException(
                    "Số lượng dữ liệu vượt quá giới hạn dòng của Excel.",
                    nameof(items));
            }

            var templatePath = Path.Combine(
                environment.ContentRootPath,
                "templates",
                "detail_template.xlsx");

            // Copy template vào RAM, đóng file trước khi xử lý workbook.
            // Các request có thể cùng đọc template.
            using var templateStream = new MemoryStream();

            using (var fileStream = new FileStream(
                templatePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                fileStream.CopyTo(templateStream);
            }

            templateStream.Position = 0;

            // Mỗi lần gọi tạo workbook riêng, không chia sẻ giữa các request.
            using var workbook = new XLWorkbook(templateStream);

            var worksheet = workbook.Worksheets.FirstOrDefault(
                x => string.Equals(
                    x.Name.Trim(),
                    "DETAIL",
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    "Template không có worksheet DETAIL.");

            // Kỳ báo cáo.
            worksheet.Range("A4:F4").Unmerge();
            worksheet.Range("A4:F4").Clear(XLClearOptions.Contents);
            worksheet.Range("A4:F4").Merge();

            worksheet.Cell("A4").Value =
                $"PERIOD: {from:dd/MM/yyyy} - {to:dd/MM/yyyy}";

            worksheet.Range("A4:F4").Style.Alignment.Horizontal =
                XLAlignmentHorizontalValues.Left;

            worksheet.Range("H4:J4").Unmerge();
            worksheet.Range("H4:J4").Clear(XLClearOptions.Contents);
            worksheet.Range("H4:J4").Merge();

            // Điều chỉnh tên cột để khớp ý nghĩa dữ liệu DTO.
            worksheet.Cell("M6").Value = "WORKING TIME\n(h)";
            worksheet.Cell("R7").Value = "Processed Qty";

            var row = firstDataRow;

            foreach (var machine in machines)
            {
                for (var date = from; date <= to; date = date.AddDays(1))
                {
                    var itemsOfDay = items
                        .Where(x =>
                            x.MachineId == machine.Id &&
                            (x.ReportDate == date ||
                             (!x.ReportDate.HasValue &&
                              x.StartTime.HasValue &&
                              DateOnly.FromDateTime(x.StartTime.Value) == date)))
                        .OrderBy(x => x.StartTime)
                        .ToList();

                    // Ngày không có dữ liệu: dành đúng một hàng trắng.
                    if (itemsOfDay.Count == 0)
                    {
                        worksheet.Row(row).Height = 32;
                        worksheet.Cell(row, 1).Value = date.ToDateTime(TimeOnly.MinValue);
                        row++;
                        continue;
                    }

                    // Tính một lần cho mỗi máy/ngày.
                    var totalTimeOfDay =
                        itemsOfDay.FirstOrDefault(x => x.DailyMachineTotalTime.HasValue)
                            ?.DailyMachineTotalTime ??
                        itemsOfDay.Sum(x => x.TotalTime) ??
                        0;
                    var h = Math.Round(
                        Math.Min(Math.Max(totalTimeOfDay, 0), 24) / 24 * 100,
                        2);
                    var lostTime = Math.Max(24 - totalTimeOfDay, 0);
                    var isFirstRowOfDay = true;

                    foreach (var item in itemsOfDay)
                    {
                        worksheet.Row(row).Height = 32;

                        // Chỉ hiển thị ngày và tỷ lệ ở hàng đầu tiên của ngày.
                        if (isFirstRowOfDay)
                        {
                            worksheet.Cell(row, 1).Value =
                                (item.ReportDate ?? date).ToDateTime(TimeOnly.MinValue);

                            worksheet.Cell(row, 15).Value = h;
                            worksheet.Cell(row, 16).Value =
                                Math.Round(lostTime / 24 * 100, 2);

                            isFirstRowOfDay = false;
                        }

                        worksheet.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";

                        worksheet.Cell(row, 3).Value = item.MachineCode ?? "";
                        worksheet.Cell(row, 4).Value = item.MachineName ?? "";
                        worksheet.Cell(row, 5).Value = GetPartTypeLabel(item.PartType);
                        worksheet.Cell(row, 6).Value = item.IndexCode ?? "";
                        worksheet.Cell(row, 7).Value = item.PartCode;
                        worksheet.Cell(row, 17).Value = item.NewRepair?.ToString() ?? "";

                        // H, I: Thời gian bắt đầu và kết thúc.
                        if (item.StartTime.HasValue)
                            worksheet.Cell(row, 8).Value = item.StartTime.Value;
                        else if (item.IsMultiDay)
                            worksheet.Cell(row, 8).Value = "-";

                        if (item.EndTime.HasValue)
                            worksheet.Cell(row, 9).Value = item.EndTime.Value;
                        else if (item.IsMultiDay)
                            worksheet.Cell(row, 9).Value = "-";

                        worksheet.Range(row, 8, row, 9)
                            .Style.DateFormat.Format = "hh:mm:ss";

                        // J: Tổng thời gian.
                        if (item.TotalTime.HasValue)
                        {
                            worksheet.Cell(row, 10).Value = item.TotalTime.Value;
                        }
                        else if (
                            item.WorkingTime.HasValue ||
                            item.SetupTime.HasValue ||
                            item.WaitingTime.HasValue ||
                            item.ProgrammingTime.HasValue)
                        {
                            worksheet.Cell(row, 10).Value =
                                (item.WorkingTime ?? 0)
                                + (item.SetupTime ?? 0)
                                + (item.WaitingTime ?? 0)
                                + (item.ProgrammingTime ?? 0);
                        }

                        // K: Setup; L: Waiting; M: Working.
                        SetNumber(worksheet.Cell(row, 11), item.SetupTime);
                        SetNumber(worksheet.Cell(row, 12), item.WaitingTime);
                        SetNumber(worksheet.Cell(row, 13), item.WorkingTime);

                        worksheet.Range(row, 15, row, 16)
                            .Style.NumberFormat.Format = "0.00";

                        worksheet.Cell(row, 18).Value = item.ProcessedQuantity;

                        row++;
                    }
                }
            }


            // Xác định hàng cuối thực tế, bao gồm cả các ngày trống.

            var lastDataRow = row - 1;
            var lastReportRow = Math.Max(firstDataRow, lastDataRow);

            // Áp dụng style và border cho toàn bộ các hàng đã tạo.
            var dataRange = worksheet.Range(
                 firstDataRow, 1,
                 lastReportRow, lastColumn);

            dataRange.Style.Font.FontName = "Arial";
            dataRange.Style.Font.FontSize = 10;
            dataRange.Style.Font.Bold = false;

            dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            dataRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            dataRange.Style.Alignment.WrapText = true;

            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            worksheet.Range(firstDataRow, 19, lastReportRow, 19)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            worksheet.Range(firstDataRow, 1, lastReportRow, 1)
                .Style.NumberFormat.Format = "dd/MM/yyyy";

            worksheet.Range(firstDataRow, 8, lastReportRow, 9)
                .Style.NumberFormat.Format = "hh:mm";

            worksheet.Range(firstDataRow, 10, lastReportRow, 13)
                .Style.NumberFormat.Format = "#,##0.00";

            worksheet.Range(firstDataRow, 15, lastReportRow, 16)
                .Style.NumberFormat.Format = "0.00";

            worksheet.Range(firstDataRow, 18, lastReportRow, 18)
                .Style.NumberFormat.Format = "#,##0";

            worksheet.SheetView.FreezeRows(7);

            if (items.Count > 0)
            {
                worksheet.Range(7, 1, lastDataRow, lastColumn)
                    .SetAutoFilter();
            }

            // Giữ chiều rộng cột của template.
            worksheet.PageSetup.PageOrientation =
                XLPageOrientation.Landscape;

            worksheet.PageSetup.FitToPages(1, 0);
            worksheet.PageSetup.SetRowsToRepeatAtTop(1, 7);

            worksheet.PageSetup.PrintAreas.Clear();
            worksheet.PageSetup.PrintAreas.Add(
                $"A1:S{lastReportRow}");

            using var outputStream = new MemoryStream();

            workbook.SaveAs(outputStream);

            return outputStream.ToArray();

            static void SetNumber(IXLCell cell, double? value)
            {
                if (value.HasValue)
                {
                    cell.Value = value.Value;
                }
            }
        }

        private static void CreateTitle(
            IXLWorksheet worksheet,
            string title,
            DateOnly from,
            DateOnly to,
            int lastColumn)
        {
            var titleRange = worksheet.Range(1, 1, 1, lastColumn);
            titleRange.Merge();
            titleRange.Value = title;
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Font.FontSize = 16;
            titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var filterRange = worksheet.Range(2, 1, 2, lastColumn);
            filterRange.Merge();
            filterRange.Value = $"Từ ngày {from:dd/MM/yyyy} đến ngày {to:dd/MM/yyyy}";
            filterRange.Style.Font.Italic = true;
            filterRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        private static void WriteHeaders(IXLWorksheet worksheet, string[] headers)
        {
            for (var column = 0; column < headers.Length; column++)
            {
                worksheet.Cell(HeaderRow, column + 1).Value = headers[column];
            }

            var headerRange = worksheet.Range(HeaderRow, 1, HeaderRow, headers.Length);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1677FF");
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRange.Style.Alignment.WrapText = true;
        }

        private static void FinishWorksheet(
            IXLWorksheet worksheet,
            int lastDataRow,
            int lastColumn)
        {
            var effectiveLastRow = Math.Max(lastDataRow, HeaderRow);
            var usedRange = worksheet.Range(HeaderRow, 1, effectiveLastRow, lastColumn);

            usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            usedRange.SetAutoFilter();

            worksheet.SheetView.FreezeRows(HeaderRow);
            worksheet.Columns(1, lastColumn).AdjustToContents();

            for (var columnIndex = 1; columnIndex <= lastColumn; columnIndex++)
            {
                var column = worksheet.Column(columnIndex);
                if (column.Width > 35)
                {
                    column.Width = 35;
                }
            }
        }

        private static byte[] Save(XLWorkbook workbook)
        {
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
