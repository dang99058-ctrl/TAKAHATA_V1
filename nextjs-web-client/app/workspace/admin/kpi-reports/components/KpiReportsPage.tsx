"use client";

import {
  AppstoreOutlined,
  ClearOutlined,
  ClockCircleOutlined,
  DashboardOutlined,
  DownloadOutlined,
  ReloadOutlined,
  SearchOutlined,
  ThunderboltOutlined,
  ToolOutlined,
} from "@ant-design/icons";
import {
  Avatar,
  Button,
  Card,
  Col,
  DatePicker,
  Empty,
  Input,
  Row,
  Space,
  Table,
  Tag,
  Typography,
} from "antd";
import type { ColumnsType, TablePaginationConfig } from "antd/es/table";
import dayjs, { type Dayjs } from "dayjs";
import {
  ComponentProps,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Rectangle,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { toast } from "react-toastify";
import duration from "dayjs/plugin/duration";
dayjs.extend(duration);

type RectangleProps = ComponentProps<typeof Rectangle>;

import {
  downloadMachinesReportAsync,
  downloadPartProcessReportAsync,
  searchMachinesReportAsync,
  searchPartProcessReportAsync,
} from "@/app/services/report/ReportService";
import {
  MachineStatus,
  PartProcessKPI,
  PartType,
  type MachineKPI,
} from "@/app/types";
import { ProcessStatus } from "@/app/types/common/ProcessStatus";

const { RangePicker } = DatePicker;

const WORKING_HOURS_PER_DAY = 24;
const DEFAULT_PAGE_SIZE = 50;
const DEFAULT_PROCESS_PAGE_SIZE = 50;
const CHART_COLORS = [
  "#4CAF50",
  "#2196F3",
  "#FFC107",
  "#FF5722",
  "#9C27B0",
  "#00BCD4",
] as const;

const MACHINE_CHART_COLORS: Record<string, string> = {
  "1": "#E4572E",
  "2": "#17A398",
  "3": "#FFC914",
  "5": "#5C4D7D",
  "19": "#76B041",
  "20": "#4D9DE0",
  "22": "#C1292E",
  "23": "#00A6A6",
  "24": "#F18F01",
  "25": "#6A4C93",
  "28": "#2E86AB",
  "38": "#A23B72",
  "41": "#3B8C6E",
  "42": "#D1495B",
  "46": "#386641",
  "55": "#7B2CBF",
  "58": "#BC6C25",
  "59": "#0077B6",
  "60": "#8A5A44",
  "61": "#5F0F40",
};

const getMachineChartColors = (machineCodes: string[]) => {
  const colors: Record<string, string> = {};
  const usedColors = new Set<string>();

  for (const code of machineCodes) {
    const color = MACHINE_CHART_COLORS[code];
    if (color) {
      colors[code] = color;
      usedColors.add(color);
    }
  }

  const unknownCodes = [...new Set(machineCodes)]
    .filter((code) => !colors[code])
    .sort();

  for (const code of unknownCodes) {
    let hash = 0;
    for (const character of code) {
      hash = (hash * 31 + character.charCodeAt(0)) | 0;
    }

    let hue = ((hash % 360) + 360) % 360;
    let color = `hsl(${hue.toFixed(3)} 68% 48%)`;
    while (usedColors.has(color)) {
      hue = (hue + 137.508) % 360;
      color = `hsl(${hue.toFixed(3)} 68% 48%)`;
    }

    colors[code] = color;
    usedColors.add(color);
  }

  return colors;
};

const CARD_STYLE = {
  border: "1px solid #e5e7eb",
  boxShadow:
    "0 10px 30px rgba(15, 23, 42, 0.08), 0 2px 8px rgba(15, 23, 42, 0.04)",
  background: "#fff",
} as const;

type DateRange = [Dayjs | null, Dayjs | null] | null;

const getCurrentMonthRange = (): DateRange => [
  dayjs().startOf("month"),
  dayjs().endOf("month"),
];

type SummaryCard = {
  title: string;
  value: string;
  color: string;
  icon: ReactNode;
};

type MachinePeriodKPI = MachineKPI & { totalTime: number };

const formatHours = (value?: number | null, fractionDigits = 2) =>
  value == null ? "-" : `${value.toFixed(fractionDigits)} giờ`;

const getMachineTotalTime = (
  machine: Pick<MachineKPI, "workingTime" | "setupTime" | "waitingTime">,
) =>
  (machine.workingTime ?? 0) +
  (machine.setupTime ?? 0) +
  (machine.waitingTime ?? 0);

import type { RangePickerProps } from "antd/es/date-picker";
import { PartTypeLabels } from "@/app/types/part/PartTypeLabels";

const disabledSixMonthRange: RangePickerProps["disabledDate"] = (
  currentDate,
  { from },
) => {
  // Chưa chọn ngày đầu tiên thì không giới hạn
  if (!from) {
    return false;
  }

  const minDate = from.subtract(6, "month");
  const maxDate = from.add(6, "month");

  return (
    currentDate.isBefore(minDate, "day") || currentDate.isAfter(maxDate, "day")
  );
};

export function KpiReportsPage() {
  const initialLoadRef = useRef(false);

  const [machinePagination, setMachinePagination] =
    useState<TablePaginationConfig>({
      current: 1,
      pageSize: DEFAULT_PAGE_SIZE,
    });
  const [processPagination, setProcessPagination] =
    useState<TablePaginationConfig>({
      current: 1,
      pageSize: DEFAULT_PROCESS_PAGE_SIZE,
    });

  const [machineDateRange, setMachineDateRange] =
    useState<DateRange>(getCurrentMonthRange);
  const [loadedMachineDateRange, setLoadedMachineDateRange] =
    useState<DateRange>(getCurrentMonthRange);
  const [processDateRange, setProcessDateRange] =
    useState<DateRange>(getCurrentMonthRange);

  const [searchMachineKeyword, setSearchMachineKeyword] = useState("");

  const [exportingMachinesReport, setExportingMachinesReport] = useState(false);
  const [exportingProcessesReport, setExportingProcessesReport] =
    useState(false);

  const [machinesKPI, setMachinesKPI] = useState<MachineKPI[]>([]);
  const [loadingMachinesReport, setLoadingMachinesReport] = useState(false);

  const [partProcessKPIs, setPartProcessKPIs] = useState<PartProcessKPI[]>([]);
  const [loadingProcessesReport, setLoadingProcessesReport] = useState(false);

  const [searchMachineOfProcessKeyword, setSearchMachineOfProcessKeyword] =
    useState("");
  const [searchIndexOfProcessKeyword, setSearchIndexOfProcessKeyword] =
    useState("");
  const [searchPartOfProcessKeyword, setSearchPartOfProcessKeyword] =
    useState("");

  const machinePeriodKPIs = useMemo(() => {
    const [from, to] = loadedMachineDateRange ?? [];
    const dayCount =
      from && to
        ? to.startOf("day").diff(from.startOf("day"), "day") + 1
        : new Set(machinesKPI.map((machine) => machine.reportDate)).size;
    const groupedMachines = new Map<string, MachinePeriodKPI>();

    for (const machine of machinesKPI) {
      const key = machine.code || machine.name;
      const current = groupedMachines.get(key);

      if (!current) {
        groupedMachines.set(key, {
          ...machine,
          totalTime: getMachineTotalTime(machine),
        });
        continue;
      }

      current.workingTime =
        (current.workingTime ?? 0) + (machine.workingTime ?? 0);
      current.totalTime += getMachineTotalTime(machine);
      current.maintenanceTime =
        (current.maintenanceTime ?? 0) + (machine.maintenanceTime ?? 0);
      current.waitingTime =
        (current.waitingTime ?? 0) + (machine.waitingTime ?? 0);
      current.setupTime = (current.setupTime ?? 0) + (machine.setupTime ?? 0);
      current.lostTime += machine.lostTime;

      if (machine.reportDate > current.reportDate) {
        current.reportDate = machine.reportDate;
        current.machineStatus = machine.machineStatus;
      }
    }

    return Array.from(groupedMachines.values())
      .map((machine) => ({
        ...machine,
        utilizationRate:
          dayCount > 0
            ? (machine.totalTime / (dayCount * WORKING_HOURS_PER_DAY)) *
              100
            : 0,
      }))
      .sort((left, right) => left.code.localeCompare(right.code));
  }, [loadedMachineDateRange, machinesKPI]);

  const chartData = useMemo(() => {
    const machineColors = getMachineChartColors([
      ...machinePeriodKPIs.map((machine) => machine.code || machine.name),
    ]);

    return machinePeriodKPIs
      .map((machine) => ({
        name: machine.code || machine.name || "Máy",
        machineName: machine.name,
        totalTime: machine.totalTime,
        utilization: Number(machine.utilizationRate.toFixed(1)),
        color: machineColors[machine.code || machine.name],
      }))
      .sort((left, right) => left.name.localeCompare(right.name));
  }, [machinePeriodKPIs]);

  const fetchMachinesReport = useCallback(
    async (range: DateRange = machineDateRange) => {
      const [from, to] = range ?? [];

      if (!from || !to) {
        toast.warning("Vui lòng chọn đầy đủ khoảng thời gian cho bảng máy.");
        return;
      }

      setLoadingMachinesReport(true);

      try {
        const data = await searchMachinesReportAsync({
          searchMachineKeyword,
          from: from.format("YYYY-MM-DD"),
          to: to.format("YYYY-MM-DD"),
        });

        setMachinesKPI(data.machineKPIs ?? []);
        setLoadedMachineDateRange(range);
        setMachinePagination((current) => ({ ...current, current: 1 }));
      } catch (error) {
        console.error("Failed to load machine KPI report:", error);
        toast.error("Không thể tải báo cáo KPI máy.");
      } finally {
        setLoadingMachinesReport(false);
      }
    },
    [machineDateRange, searchMachineKeyword],
  );

  const fetchPartProcessReport = useCallback(
    async (range: DateRange = processDateRange) => {
      const [from, to] = range ?? [];

      if (!from || !to) {
        toast.warning(
          "Vui lòng chọn đầy đủ khoảng thời gian cho bảng gia công.",
        );
        return;
      }

      setLoadingProcessesReport(true);

      try {
        const data = await searchPartProcessReportAsync({
          searchMachineKeyword: searchMachineOfProcessKeyword,
          searchIndexKeyword: searchIndexOfProcessKeyword,
          searchPartKeyword: searchPartOfProcessKeyword,
          from: from.format("YYYY-MM-DD"),
          to: to.format("YYYY-MM-DD"),
        });

        setPartProcessKPIs(data.partProcessKPIs ?? []);
        setProcessPagination((current) => ({ ...current, current: 1 }));
      } catch (error) {
        console.error("Failed to load part process KPI report:", error);
        toast.error("Không thể tải báo cáo KPI part process.");
      } finally {
        setLoadingProcessesReport(false);
      }
    },
    [
      processDateRange,
      searchIndexOfProcessKeyword,
      searchMachineOfProcessKeyword,
      searchPartOfProcessKeyword,
    ],
  );

  const exportMachinesReport = useCallback(async () => {
    const [from, to] = machineDateRange ?? [];

    if (!from || !to) {
      toast.warning("Vui lòng chọn đầy đủ khoảng thời gian.");
      return;
    }

    if (to.isBefore(from, "day")) {
      toast.warning("Ngày kết thúc phải lớn hơn hoặc bằng ngày bắt đầu.");
      return;
    }

    try {
      setExportingMachinesReport(true);

      await downloadMachinesReportAsync({
        from: from.format("YYYY-MM-DD"),
        to: to.format("YYYY-MM-DD"),
      });

      toast.success("Xuất báo cáo KPI máy thành công.");
    } catch (error) {
      console.error("Failed to export machine KPI report:", error);
      toast.error("Không thể xuất báo cáo KPI máy.");
    } finally {
      setExportingMachinesReport(false);
    }
  }, [machineDateRange]);

  const exportPartProcessReport = useCallback(async () => {
    const [from, to] = processDateRange ?? [];

    if (!from || !to) {
      toast.warning("Vui lòng chọn đầy đủ khoảng thời gian.");
      return;
    }

    if (to.isBefore(from, "day")) {
      toast.warning("Ngày kết thúc phải lớn hơn hoặc bằng ngày bắt đầu.");
      return;
    }

    try {
      setExportingProcessesReport(true);

      await downloadPartProcessReportAsync({
        searchMachineKeyword: searchMachineOfProcessKeyword,
        searchIndexKeyword: searchIndexOfProcessKeyword,
        searchPartKeyword: searchPartOfProcessKeyword,
        from: from.format("YYYY-MM-DD"),
        to: to.format("YYYY-MM-DD"),
      });

      toast.success("Xuất báo cáo gia công thành công.");
    } catch (error) {
      console.error("Failed to export part process report:", error);
      toast.error("Không thể xuất báo cáo gia công.");
    } finally {
      setExportingProcessesReport(false);
    }
  }, [
    processDateRange,
    searchIndexOfProcessKeyword,
    searchMachineOfProcessKeyword,
    searchPartOfProcessKeyword,
  ]);

  const resetMachineFilters = useCallback(() => {
    const range = getCurrentMonthRange();
    setMachineDateRange(range);
    void fetchMachinesReport(range);
  }, [fetchMachinesReport]);

  const resetProcessFilters = useCallback(() => {
    const range = getCurrentMonthRange();
    setProcessDateRange(range);
    void fetchPartProcessReport(range);
  }, [fetchPartProcessReport]);

  useEffect(() => {
    if (initialLoadRef.current) return;
    initialLoadRef.current = true;

    const currentMonth = getCurrentMonthRange();
    void Promise.all([
      fetchMachinesReport(currentMonth),
      fetchPartProcessReport(currentMonth),
    ]);
  }, [fetchMachinesReport, fetchPartProcessReport]);

  const machineSummary = useMemo(() => {
    const [from, to] = loadedMachineDateRange ?? [];
    const dayCount =
      from && to
        ? to.startOf("day").diff(from.startOf("day"), "day") + 1
        : new Set(machinesKPI.map((machine) => machine.reportDate)).size;
    const machineCount = new Set(
      machinesKPI.map((machine) => machine.code || machine.name),
    ).size;
    const totalAvailableTime =
      dayCount * WORKING_HOURS_PER_DAY * machineCount;
    const totalMachineTime = machinesKPI.reduce(
      (sum, machine) => sum + getMachineTotalTime(machine),
      0,
    );
    const totalMaintenanceTime = machinesKPI.reduce(
      (sum, machine) => sum + (machine.maintenanceTime ?? 0),
      0,
    );
    const totalLostTime = machinesKPI.reduce(
      (sum, machine) =>
        sum +
        Math.max(WORKING_HOURS_PER_DAY - getMachineTotalTime(machine), 0),
      0,
    );
    const machineUtilization =
      totalAvailableTime > 0
        ? (totalMachineTime / totalAvailableTime) * 100
        : 0;

    return {
      totalAvailableTime,
      totalMachineTime,
      totalMaintenanceTime,
      totalLostTime,
      machineUtilization,
    };
  }, [loadedMachineDateRange, machinesKPI]);

  const summaryCards = useMemo<SummaryCard[]>(
    () => [
      {
        title: "Hiệu suất sử dụng",
        value: `${machineSummary.machineUtilization.toFixed(1)}%`,
        color: "#22c55e",
        icon: <DashboardOutlined />,
      },
      {
        title: "Tổng thời gian khả dụng",
        value: `${machineSummary.totalAvailableTime.toFixed(1)} giờ`,
        color: "#3b82f6",
        icon: <ClockCircleOutlined />,
      },
      {
        title: "Tổng thời gian hoạt động",
        value: `${machineSummary.totalMachineTime.toFixed(1)} giờ`,
        color: "#6366f1",
        icon: <ThunderboltOutlined />,
      },
      {
        title: "Tổng thời gian bảo trì",
        value: `${machineSummary.totalMaintenanceTime.toFixed(1)} giờ`,
        color: "#ef4444",
        icon: <ToolOutlined />,
      },
      {
        title: "Tổng Lost Time",
        value: `${machineSummary.totalLostTime.toFixed(1)} giờ`,
        color: "#f97316",
        icon: <ClockCircleOutlined />,
      },
    ],
    [machineSummary],
  );

  const machineKPIColumns = useMemo<ColumnsType<MachinePeriodKPI>>(
    () => [
      {
        title: "Máy",
        dataIndex: "name",
        key: "name",
        fixed: "left",
        width: 220,
        render: (_, record) => (
          <Space>
            <Avatar
              size={28}
              icon={<AppstoreOutlined />}
              style={{ background: "#e8f1ff", color: "#1677ff" }}
            />
            <Typography.Text strong ellipsis={{ tooltip: record.name }}>
              {record.code} - {record.name}
            </Typography.Text>
          </Space>
        ),
      },
      {
        title: "Loại máy",
        dataIndex: "type",
        key: "type",
        width: 150,
        render: (value?: string) => value || "-",
      },
      {
        title: "Thời gian hoạt động",
        dataIndex: "totalTime",
        key: "totalTime",
        width: 150,
        align: "right",
        sorter: (a, b) => a.totalTime - b.totalTime,
        render: (value?: number | null) => formatHours(value),
      },
      {
        title: "Thời gian bảo trì",
        dataIndex: "maintenanceTime",
        key: "maintenanceTime",
        width: 150,
        align: "right",
        sorter: (a, b) => (a.maintenanceTime ?? 0) - (b.maintenanceTime ?? 0),
        render: (value?: number | null) => formatHours(value),
      },
      {
        title: "Trạng thái",
        dataIndex: "machineStatus",
        key: "machineStatus",
        width: 160,
        align: "center",
        filters: [
          { text: "Đang hoạt động", value: MachineStatus.Running },
          { text: "Bảo trì", value: MachineStatus.Maintenance },
          { text: "Dừng", value: MachineStatus.Stopped },
        ],
        onFilter: (value, record) => record.machineStatus === value,
        render: (status?: MachineStatus) => {
          const statusConfig: Partial<
            Record<MachineStatus, { color: string; label: string }>
          > = {
            [MachineStatus.Running]: {
              color: "success",
              label: "Đang hoạt động",
            },
            [MachineStatus.Maintenance]: {
              color: "warning",
              label: "Bảo trì",
            },
            [MachineStatus.Stopped]: { color: "error", label: "Dừng" },
          };
          const config = status == null ? undefined : statusConfig[status];

          return config ? (
            <Tag color={config.color}>{config.label}</Tag>
          ) : (
            <Tag>Không xác định</Tag>
          );
        },
      },
      {
        title: "Hiệu suất",
        dataIndex: "utilizationRate",
        key: "utilizationRate",
        fixed: "right",
        width: 120,
        align: "right",
        sorter: (a, b) => a.utilizationRate - b.utilizationRate,
        render: (value: number) => `${value.toFixed(1)}%`,
      },
    ],
    [],
  );

  const partProcessesColumns: ColumnsType<PartProcessKPI> = [
    {
      title: "Ngày báo cáo",
      width: 120,
      dataIndex: "reportDate",
      key: "reportDate",
      fixed: "left",
      render: (date?: string | null) =>
        date && dayjs(date).isValid() ? dayjs(date).format("DD/MM/YYYY") : "-",
    },
    {
      title: "Kế hoạch",
      width: 100,
      fixed: "left",
      dataIndex: "plantDate",
      key: "plantDate",
      render: (date?: string | Date | Dayjs | null) =>
        date && dayjs(date).isValid() ? dayjs(date).format("DD/MM/YYYY") : "-",
    },
    {
      title: "Mã máy",
      width: 80,
      fixed: "left",
      dataIndex: "machineCode",
      key: "machineCode",
      align: "center",
    },
    {
      title: "Tên máy",
      width: 180,
      fixed: "left",
      dataIndex: "machineName",
      key: "machineName",
      align: "center",
      render: (_, record) => record.machineName ?? record.machineCode ?? "-",
    },
    {
      title: "Index",
      width: 80,
      fixed: "left",
      dataIndex: "indexCode",
      key: "indexCode",
      align: "center",
    },
    {
      title: "Part",
      width: 180,
      dataIndex: "partCode",
      key: "partCode",
      fixed: "left",
      align: "center",
    },
    {
      title: "P.I.C",
      width: 140,
      dataIndex: "performedBy",
      key: "performedBy",
      align: "center",
      render: (performedBy?: string | null) => performedBy || "-",
    },
    {
      title: "Thời gian",
      children: [
        {
          title: "Ước tính (giờ)",
          width: 150,
          dataIndex: "estimatedTime",
          key: "estimatedTime",
          render: (value) => (value != null ? `${value} giờ` : "-"),
        },
        {
          title: "Bắt đầu",
          width: 150,
          dataIndex: "startTime",
          key: "startTime",
          render: (date) =>
            date && dayjs(date).isValid()
              ? dayjs(date).format("DD/MM/YYYY HH:mm:ss")
              : "-",
        },
        {
          title: "Kết thúc",
          width: 150,
          dataIndex: "endTime",
          key: "endTime",
          render: (date) =>
            date && dayjs(date).isValid()
              ? dayjs(date).format("DD/MM/YYYY HH:mm:ss")
              : "-",
        },
      ],
    },

    {
      title: "Thời gian (giờ)",
      children: [
        {
          title: "Máy chạy (giờ)",
          width: 120,
          dataIndex: "workingTime",
          key: "workingTime",
          align: "right",
          render: (value?: number) => value?.toFixed(2) ?? "-",
        },
        {
          title: "Gá đặt (giờ)",
          width: 120,
          dataIndex: "setupTime",
          key: "setupTime",
          align: "right",
          render: (value?: number) => value?.toFixed(2) ?? "-",
        },
        {
          title: "Chờ (giờ)",
          width: 120,
          dataIndex: "waitingTime",
          key: "waitingTime",
          align: "right",
          render: (value?: number) => value?.toFixed(2) ?? "-",
        },
        {
          title: "Lập trình (giờ)",
          width: 120,
          dataIndex: "programmingTime",
          key: "programmingTime",
          align: "right",
          render: (value?: number) => value?.toFixed(2) ?? "-",
        },
        {
          title: "Tổng (giờ)",
          width: 120,
          dataIndex: "totalTime",
          key: "totalTime",
          align: "right",
          render: (value?: number) => value?.toFixed(2) ?? "-",
        },
      ],
    },
    {
      title: "Số lượng",
      width: 120,
      dataIndex: "quantity",
      key: "quantity",
      align: "right",
    },
    {
      title: "Đã hoàn thành",
      width: 120,
      dataIndex: "processedQuantity",
      key: "processedQuantity",
      align: "right",
    },
    {
      title: "Trạng thái",
      width: 120,
      dataIndex: "status",
      key: "status",
      render: (status: ProcessStatus) => <Tag>{ProcessStatus[status]}</Tag>,
    },
    {
      title: "Loại gia công",
      dataIndex: "partType",
      key: "partType",
      width: 250,
      align: "center",
      render: (value?: PartType | null) =>
        PartTypeLabels[value as PartType] || "-",
    },
    {
      title: "Hiệu suất",
      width: 80,
      key: "efficiency",
      fixed: "right",
      align: "right",
      render: (_, record) => {
        const workingTime = record.workingTime ?? 0;
        const totalMachineTime =
          workingTime +
          (record.setupTime ?? 0) +
          (record.waitingTime ?? 0);
        const efficiency =
          totalMachineTime > 0 ? workingTime / totalMachineTime : 0;

        return `${(efficiency * 100).toFixed(2)}%`;
      },
    },
  ];

  return (
    <div
      className="d-flex flex-column"
      style={{ height: "calc(100vh - 20px)" }}
    >
      <div className="grow overflow-auto pe-2">
        <Card className="border-0 shadow-sm">
          <Row gutter={[12, 12]} align="stretch">
            <Col xs={24} lg={14}>
              <Card
                className="h-100 rounded-4"
                title={
                  <span className="fw-semibold">
                    Hiệu suất sử dụng từng máy
                  </span>
                }
                style={CARD_STYLE}
              >
                {chartData.length > 0 ? (
                  <ResponsiveContainer width="100%" height={320}>
                    <BarChart
                      data={chartData}
                      margin={{ top: 10, right: 20, left: 20, bottom: 28 }}
                    >
                      <CartesianGrid strokeDasharray="3 3" vertical={false} />
                      <XAxis
                        dataKey="name"
                        axisLine={false}
                        tickLine={false}
                        interval={0}
                        tick={{ fontSize: 11 }}
                        label={{
                          value: "Máy",
                          position: "insideBottom",
                          offset: -18,
                        }}
                      />
                      <YAxis
                        domain={[0, 100]}
                        ticks={[0, 20, 40, 60, 80, 100]}
                        unit="%"
                        allowDataOverflow
                        label={{
                          value: "Hiệu suất sử dụng (%)",
                          angle: -90,
                          position: "insideLeft",
                          style: { textAnchor: "middle" },
                        }}
                      />
                      <Tooltip
                        labelFormatter={(label) => {
                          const machine = chartData.find(
                            (item) => item.name === label,
                          );
                          return machine
                            ? `${machine.name} - ${machine.machineName} · ${machine.totalTime.toFixed(2)} giờ hoạt động`
                            : String(label ?? "");
                        }}
                        formatter={(value) => {
                          const rawValue = Array.isArray(value)
                            ? value[0]
                            : value;
                          const numericValue = Number(rawValue ?? 0);

                          return [
                            `${Number.isFinite(numericValue) ? numericValue.toFixed(1) : "0.0"}%`,
                            "Hiệu suất",
                          ];
                        }}
                      />
                      <Bar
                        dataKey="utilization"
                        shape={(
                          props: RectangleProps & {
                            payload?: { color: string };
                          },
                        ) => (
                            <Rectangle
                              {...props}
                              fill={props.payload?.color ?? CHART_COLORS[0]}
                              radius={[8, 8, 0, 0]}
                            />
                          )}
                      />
                    </BarChart>
                  </ResponsiveContainer>
                ) : (
                  <Empty
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                    description="Không có dữ liệu trong khoảng thời gian đã chọn"
                  />
                )}
              </Card>
            </Col>

            <Col xs={24} lg={10}>
              <Card
                className="h-100 rounded-4"
                title={<span className="fw-semibold">Tổng quan</span>}
                style={CARD_STYLE}
              >
                <Row gutter={[12, 12]}>
                  {summaryCards.map((item) => (
                    <Col xs={24} sm={12} key={item.title}>
                      <div
                        className="h-100 rounded-4 p-3"
                        style={{
                          border: "1px solid #edf2f7",
                          borderLeft: `5px solid ${item.color}`,
                          background: "#fff",
                        }}
                      >
                        <div
                          className="mb-3 d-flex align-items-center justify-content-center rounded-circle"
                          style={{
                            width: 42,
                            height: 42,
                            background: `${item.color}15`,
                            color: item.color,
                            fontSize: 18,
                          }}
                        >
                          {item.icon}
                        </div>
                        <Typography.Text type="secondary">
                          {item.title}
                        </Typography.Text>
                        <div
                          className="mt-1 fw-bold"
                          style={{ fontSize: 24, color: item.color }}
                        >
                          {item.value}
                        </div>
                      </div>
                    </Col>
                  ))}
                </Row>
              </Card>
            </Col>
          </Row>

          <Card
            className="mt-3 rounded-4"
            style={CARD_STYLE}
            title={
              <div className="d-flex flex-wrap align-items-center justify-content-between gap-3 py-1">
                <div>
                  <Typography.Title level={5} className="mb-0">
                    Chi tiết thời gian gia công
                  </Typography.Title>
                  <Typography.Text type="secondary">
                    Theo dõi tiến độ, thời gian chạy máy, gá đặt và chờ
                  </Typography.Text>
                </div>

                <Space wrap size={8}>
                  <Input
                    placeholder="Nhập tên hoặc mã máy"
                    allowClear
                    value={searchMachineOfProcessKeyword}
                    onChange={(event) =>
                      setSearchMachineOfProcessKeyword(event.target.value)
                    }
                  />
                  <Input
                    placeholder="Index"
                    allowClear
                    value={searchIndexOfProcessKeyword}
                    onChange={(event) =>
                      setSearchIndexOfProcessKeyword(event.target.value)
                    }
                  />
                  <Input
                    placeholder="Part name"
                    allowClear
                    value={searchPartOfProcessKeyword}
                    onChange={(event) =>
                      setSearchPartOfProcessKeyword(event.target.value)
                    }
                  />

                  <RangePicker
                    value={processDateRange}
                    disabledDate={disabledSixMonthRange}
                    onChange={(dates) => setProcessDateRange(dates)}
                    format="DD/MM/YYYY"
                    placeholder={["Từ ngày", "Đến ngày"]}
                    allowClear={false}
                  />
                  <Button
                    icon={<ClearOutlined />}
                    disabled={loadingProcessesReport}
                    onClick={resetProcessFilters}
                  >
                    Tháng hiện tại
                  </Button>
                  <Button
                    type="primary"
                    icon={<ReloadOutlined />}
                    loading={loadingProcessesReport}
                    onClick={() => void fetchPartProcessReport()}
                  >
                    Tải dữ liệu
                  </Button>
                  <Button
                    icon={<DownloadOutlined />}
                    loading={exportingProcessesReport}
                    onClick={() => void exportPartProcessReport()}
                    disabled={
                      partProcessKPIs.length === 0 || loadingProcessesReport
                    }
                  >
                    Xuất báo cáo
                  </Button>
                </Space>
              </div>
            }
          >
            <Table<PartProcessKPI>
              rowKey={(record) =>
                `${record.partProcessId}-${record.reportDate ?? "single-day"}`
              }
              loading={loadingProcessesReport}
              columns={partProcessesColumns}
              dataSource={partProcessKPIs}
              bordered
              size="small"
              locale={{ emptyText: "Không có dữ liệu gia công" }}
              pagination={{
                current: processPagination.current,
                pageSize: processPagination.pageSize,
                total: partProcessKPIs.length,
                showSizeChanger: true,
                showQuickJumper: true,
                pageSizeOptions: [10, 20, 50, 100, 500],
                showTotal: (total, range) =>
                  `${range[0]}-${range[1]} / ${total} công đoạn`,
                onChange: (current, pageSize) =>
                  setProcessPagination({ current, pageSize }),
              }}
              scroll={{ x: 1600 }}
            />
          </Card>

          <Card
            className="dashboard-card mt-3 rounded-4"
            style={CARD_STYLE}
            title={
              <div className="d-flex flex-wrap align-items-center justify-content-between gap-3 py-1">
                <div>
                  <Typography.Title level={5} className="mb-0">
                    KPI hiệu suất máy
                  </Typography.Title>
                </div>

                <Space wrap size={8}>
                  <Input
                    placeholder="Nhập tên hoặc mã máy"
                    allowClear
                    value={searchMachineKeyword}
                    onChange={(event) =>
                      setSearchMachineKeyword(event.target.value)
                    }
                  />
                  <RangePicker
                    value={machineDateRange}
                    disabledDate={disabledSixMonthRange}
                    onChange={(dates) => setMachineDateRange(dates)}
                    format="DD/MM/YYYY"
                    placeholder={["Từ ngày", "Đến ngày"]}
                    allowClear={false}
                  />
                  <Button
                    icon={<ClearOutlined />}
                    disabled={loadingMachinesReport}
                    onClick={resetMachineFilters}
                  >
                    Tháng hiện tại
                  </Button>
                  <Button
                    type="primary"
                    icon={<SearchOutlined />}
                    loading={loadingMachinesReport}
                    onClick={() => void fetchMachinesReport()}
                  >
                    Tải dữ liệu
                  </Button>
                  <Button
                    icon={<DownloadOutlined />}
                    loading={exportingMachinesReport}
                    onClick={() => void exportMachinesReport()}
                    disabled={machinesKPI.length === 0 || loadingMachinesReport}
                  >
                    Xuất báo cáo
                  </Button>
                </Space>
              </div>
            }
          >
            <Table<MachinePeriodKPI>
              rowKey={(record) => record.code || record.name}
              columns={machineKPIColumns}
              dataSource={machinePeriodKPIs}
              loading={loadingMachinesReport}
              size="middle"
              className="dashboard-table"
              scroll={{ x: 1400 }}
              locale={{ emptyText: "Không có dữ liệu KPI máy" }}
              pagination={{
                current: machinePagination.current,
                pageSize: machinePagination.pageSize,
                total: machinePeriodKPIs.length,
                showSizeChanger: true,
                showQuickJumper: true,
                pageSizeOptions: [5, 10, 20, 100, 500],
                showTotal: (total, range) =>
                  `${range[0]}-${range[1]} / ${total} máy`,
                onChange: (current, pageSize) =>
                  setMachinePagination({ current, pageSize }),
              }}
            />
          </Card>
        </Card>
      </div>
    </div>
  );
}
