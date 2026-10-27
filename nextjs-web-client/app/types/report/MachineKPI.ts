import { MachineStatus } from "../machine/MachineStatus";

export interface MachineKPI {
  reportDate: string;
  name: string;
  code: string;
  type: string;
  brand: string;
  workingTime: number | null;
  waitingTime: number | null;
  setupTime: number | null;
  maintenanceTime: number | null;
  utilizationRate: number;
  lostTime: number;
  machineStatus: MachineStatus;
}
