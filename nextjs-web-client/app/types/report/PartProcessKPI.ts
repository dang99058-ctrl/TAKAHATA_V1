import { Dayjs } from "dayjs";
import { ProcessStatus } from "../common/ProcessStatus";
import { PartType } from "../part/PartType";

export interface PartProcessKPI {
  machineId: number;
  machineName?: string;
  machineCode?: string;
  indexId: number;
  indexCode?: string;
  partId: number;
  partCode: string;
  partProcessId: number;
  performedBy?: string | null;
  quantity: number;
  processedQuantity: number;
  reportDate?: string;
  isMultiDay?: boolean;
  dailyMachineTotalTime?: number;
  workingTime?: number;
  setupTime?: number;
  waitingTime?: number;
  programmingTime?: number;
  totalTime?: number;
  plantDate?: Dayjs;
  startTime?: Dayjs;
  endTime?: Dayjs;
  status: ProcessStatus;
  estimatedTime?: number;
  partType?: PartType;
}
