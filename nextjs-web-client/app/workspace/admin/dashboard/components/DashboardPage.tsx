"use client";

import { useEffect, useMemo, useState } from "react";
import dayjs from "dayjs";

import {
  getLatestMachineLogsAsync,
  getMachinesAsync,
} from "@/app/services/machine/MachineService";
import { MachineLog } from "@/app/types/machine/MachineLog";
import { MachineStatus } from "@/app/types/machine/MachineStatus";
import { Machine } from "@/app/types";
import { Button, Card, Tooltip } from "antd";
import DetailPlant from "./DetailPlant";

export const statusConfig: Record<
  MachineStatus,
  {
    text: string;
    color: string;
    foreground: string;
    icon: string;
  }
> = {
  [MachineStatus.None]: {
    text: "NONE",
    color: "#6c757d",
    foreground: "#fff",
    icon: "bi-circle",
  },

  [MachineStatus.Running]: {
    text: "RUNNING",
    color: "#198754",
    foreground: "#fff",
    icon: "bi-play-circle-fill",
  },

  [MachineStatus.Paused]: {
    text: "PAUSED",
    color: "#ffd166",
    foreground: "#212529",
    icon: "bi-pause-circle-fill",
  },

  [MachineStatus.Stopped]: {
    text: "STOPPED",
    color: "#fd7e14",
    foreground: "#fff",
    icon: "bi-stop-circle-fill",
  },

  [MachineStatus.Alarm]: {
    text: "ALARM",
    color: "#dc3545",
    foreground: "#fff",
    icon: "bi-exclamation-triangle-fill",
  },

  [MachineStatus.Maintenance]: {
    text: "MAINTENANCE",
    color: "#0d6efd",
    foreground: "#fff",
    icon: "bi-tools",
  },

  [MachineStatus.Idle]: {
    text: "IDLE",
    color: "#ffc107",
    foreground: "#212529",
    icon: "bi-record-circle-fill",
  },
};

export function DashboardPage() {
  const [now, setNow] = useState(dayjs());
  const [machineLogs, setMachineLogs] = useState<MachineLog[]>([]);
  const [machines, setMachines] = useState<Machine[] | null>(null);

  const [showPlan, setShowPlan] = useState(false);
  const [machineLog, setMachineLog] = useState<MachineLog | null>(null);

  const [selectedStatus, setSelectedStatus] = useState<MachineStatus | null>(
    null,
  );

  const filteredMachineLogs = useMemo(() => {
    if (selectedStatus === null) {
      return machineLogs;
    }

    return machineLogs.filter(
      (log) => getMachineStatus(log) === selectedStatus,
    );
  }, [machineLogs, selectedStatus]);

  useEffect(() => {
    const timer = setInterval(() => setNow(dayjs()), 1000);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => {
    const load = async () => {
      try {
        if (!machines) {
          const ms = await getMachinesAsync();
          if (ms) {
            setMachines(ms);
          }
        } else {
          const machineIds = machines.map((machine) => machine.id);
          const data = await getLatestMachineLogsAsync({
            machineIds: machineIds,
          });
          setMachineLogs(data);
        }
      } catch (error) {
        console.log(error);
      }
    };

    load();
    const timer = setInterval(load, 5000);
    return () => clearInterval(timer);
  }, [machines]);

  const summary = useMemo(() => {
    const countStatus = (status: MachineStatus) =>
      machineLogs.filter((log) => getMachineStatus(log) === status).length;

    return {
      running: countStatus(MachineStatus.Running),
      paused: countStatus(MachineStatus.Paused),
      stopped: countStatus(MachineStatus.Stopped),
      idle: countStatus(MachineStatus.Idle),
      alarm: countStatus(MachineStatus.Alarm),
      maintenance: countStatus(MachineStatus.Maintenance),
      total: machineLogs.length,
    };
  }, [machineLogs]);

  return (
    <div
      className="container-fluid py-3 d-flex flex-column"
      style={{ height: "calc(100vh - 20px)" }}
    >
      {/* Summary */}
      {/* Summary */}
      <div className="row g-3 mb-3 shrink-0">
        <SummaryCard
          label="Running"
          value={summary.running}
          color="success"
          active={selectedStatus === MachineStatus.Running}
          onClick={() => setSelectedStatus(MachineStatus.Running)}
        />

        <SummaryCard
          label="Paused"
          value={summary.paused}
          color="warning"
          active={selectedStatus === MachineStatus.Paused}
          onClick={() => setSelectedStatus(MachineStatus.Paused)}
        />

        <SummaryCard
          label="Stopped"
          value={summary.stopped}
          color="secondary"
          active={selectedStatus === MachineStatus.Stopped}
          onClick={() => setSelectedStatus(MachineStatus.Stopped)}
        />

        <SummaryCard
          label="Alarm"
          value={summary.alarm}
          color="danger"
          active={selectedStatus === MachineStatus.Alarm}
          onClick={() => setSelectedStatus(MachineStatus.Alarm)}
        />

        {/* <SummaryCard
          label="Maintenance"
          value={summary.maintenance}
          color="info"
          active={selectedStatus === MachineStatus.Maintenance}
          onClick={() => setSelectedStatus(MachineStatus.Maintenance)}
        /> */}

        <SummaryCard
          label="IDLE"
          value={summary.idle}
          color="secondary"
          active={selectedStatus === MachineStatus.Idle}
          onClick={() => setSelectedStatus(MachineStatus.Idle)}
        />

        <SummaryCard
          label="Total"
          value={summary.total}
          color="dark"
          active={selectedStatus === null}
          onClick={() => setSelectedStatus(null)}
        />
      </div>

      {/* Danh sách máy */}
      <div className="card grow d-flex flex-column overflow-hidden">
        <div className="card-header d-flex justify-content-between align-items-center shrink-0">
          <strong>Trạng thái realtime</strong>
          <span>{now.format("DD/MM/YYYY HH:mm:ss")}</span>
        </div>

        <div className="card-body overflow-hidden">
          <div className="overflow-auto h-100">
            <div className="row row-cols-1 row-cols-md-2 row-cols-lg-5 g-3">
              {filteredMachineLogs.map((log) => (
                <div
                  key={`${log.id}-${String(log.machineStatusChangeAt)}`}
                  className="col"
                >
                  <MachineCard
                    machineLog={log}
                    onViewTodayPlan={(log) => {
                      setMachineLog(log);
                      setShowPlan(true);
                    }}
                  />
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>

      <DetailPlant
        open={showPlan}
        machineId={machineLog?.id || null}
        onClose={() => setShowPlan(false)}
      />
    </div>
  );
}

function getMachineStatus(log: MachineLog): MachineStatus {
  return (log.machineStatus === MachineStatus.Stopped ||
    log.machineStatus === MachineStatus.Paused) &&
    log.idle
    ? MachineStatus.Idle
    : log.machineStatus;
}

function MachineCard({
  machineLog,
  onViewTodayPlan,
}: {
  machineLog: MachineLog;
  onViewTodayPlan: (log: MachineLog) => void;
}) {
  const machineStatus = getMachineStatus(machineLog);

  const cfg = statusConfig[machineStatus];

  const handleViewTodayPlan = (machine: MachineLog) => {
    onViewTodayPlan(machine);
  };

  return (
    <Card
      className={`
    h-full
    overflow-hidden
    rounded-xl
    border
    border-slate-200
    border-l-[5px]
    shadow-sm
    transition-all duration-200
    hover:-translate-y-0.5
    hover:shadow-md
  `}
      style={{ borderLeftColor: cfg?.color ?? "#94a3b8" }}
      styles={{
        body: {
          padding: 0,
          height: "100%",
        },
      }}
    >
      <div className="flex h-full flex-col">
        {/* Hàng 1: Trạng thái máy */}
        <div
          className={`
        flex min-h-19
        items-center justify-center
        overflow-hidden
        px-4 py-3
      `}
          style={{ backgroundColor: cfg?.color ?? "#f1f5f9" }}
        >
          <div
            className={`
          flex min-h-16
          w-full
          items-center justify-center
          rounded-lg
          px-4 py-3
          text-center
          text-xl font-extrabold
          uppercase tracking-wider
          shadow-sm
          md:text-2xl
        `}
            style={{
              backgroundColor: cfg?.color ?? "#334155",
              color: cfg?.foreground ?? "#fff",
            }}
          >
            <i className={`bi ${cfg?.icon ?? "bi-circle"} me-2`} />

            <span className="truncate">
              {cfg?.text ?? machineLog?.machineStatus ?? "-"}
            </span>
          </div>
        </div>

        {/* Hàng 2: Tên máy */}
        <div
          className="
        flex min-h-25
        flex-col items-center justify-center
        border-b border-slate-200
        bg-white
        px-4 py-4
        text-center
      "
        >
          <Tooltip title={machineLog?.name}>
            <div
              className="
              w-full
              truncate
              text-2xl font-bold
              text-slate-800
              md:text-3xl
            "
            >
              {machineLog?.name ?? "-"}
            </div>
          </Tooltip>

          <div className="mt-1 text-sm font-semibold text-slate-500">
            {machineLog?.code ?? "-"}
          </div>

          {machineLog?.type && (
            <div className="mt-1 w-full truncate text-xs text-slate-400">
              {machineLog.type}
            </div>
          )}
        </div>

        {/* Hàng 3: Part code + nút kế hoạch */}
        <div
          className="
            d-flex flex-column min-h-23
            items-center justify-between
            gap-3
            bg-slate-50
            px-4 py-3
          "
        >
          <div className="flex min-w-0 flex-1 items-center gap-2">
            <div className="shrink-0 text-xs font-semibold uppercase tracking-wider text-slate-500">
              Tên chi tiết:
            </div>

            <Tooltip title={machineLog?.part?.code}>
              <div className="min-w-0 flex-1 truncate text-xl font-bold text-slate-800">
                {machineLog?.part?.index?.code ?? "-"}-
                {machineLog?.part?.code ?? "-"}
              </div>
            </Tooltip>
          </div>

          <Button
            type="primary"
            size="small"
            className="
              flex!
              shrink-0
              items-center
              justify-center
              rounded-lg!
              px-3!
              font-semibold
              shadow-sm
              transition-transform duration-200
              hover:scale-105
            "
            icon={<i className="bi bi-calendar3" />}
            onClick={() => handleViewTodayPlan(machineLog)}
          >
            <span className="hidden md:inline">Kế hoạch hôm nay</span>
            <span className="md:hidden">Kế hoạch</span>
          </Button>
        </div>
      </div>
    </Card>
  );
}

function SummaryCard({
  label,
  value,
  color = "dark",
  active = false,
  onClick,
}: {
  label: string;
  value: number;
  color?: string;
  active?: boolean;
  onClick: () => void;
}) {
  return (
    <div className="col">
      <button
        type="button"
        aria-pressed={active}
        onClick={onClick}
        className={`card border-${color} h-100 w-100 p-0 ${
          active ? "shadow border-3" : ""
        }`}
        style={{
          cursor: "pointer",
          backgroundColor: active
            ? `var(--bs-${color}-bg-subtle, #f0f0f0)`
            : "var(--bs-body-bg, #fff)",
          transition: "background-color 0.2s, box-shadow 0.2s",
        }}
      >
        <div className="card-body text-center w-100">
          <div className={`text-${color} fw-semibold`}>{label}</div>

          <h2 className={`text-${color} mb-0`}>{value}</h2>
        </div>
      </button>
    </div>
  );
}
