'use client';

import { Table, Spinner } from 'react-bootstrap';
import useSWR from 'swr';

interface WeatherForecast {
  date: string;
  temperatureC: number;
  temperatureF: number;
  summary: string | null;
}

const fetcher = (url: string) => fetch(url).then((res) => res.json());

export default function WeatherPage() {
  const { data: forecasts, error, isLoading } = useSWR<WeatherForecast[]>(
    '/api/weather',
    fetcher
  );

  if (error) {
    return (
      <div>
        <h1>Weather</h1>
        <p className="text-danger">Failed to load weather data.</p>
      </div>
    );
  }

  if (isLoading || !forecasts) {
    return (
      <div>
        <h1>Weather</h1>
        <div>
          <em>
            <Spinner animation="border" size="sm" className="me-2" />
            Loading...
          </em>
        </div>
      </div>
    );
  }

  // Check if the response is an error object instead of an array
  if (!Array.isArray(forecasts)) {
    return (
      <div>
        <h1>Weather</h1>
        <p className="text-danger">
          Failed to load weather data: {(forecasts as any).error || 'Unknown error'}
        </p>
      </div>
    );
  }

  return (
    <div>
      <h1>Weather</h1>

      <p>This component demonstrates showing data loaded from a backend API service.</p>

      <Table striped bordered hover>
        <thead>
          <tr>
            <th>Date</th>
            <th aria-label="Temperature in Celsius">Temp. (C)</th>
            <th aria-label="Temperature in Fahrenheit">Temp. (F)</th>
            <th>Summary</th>
          </tr>
        </thead>
        <tbody>
          {forecasts.map((forecast, index) => (
            <tr key={index}>
              <td>{new Date(forecast.date).toLocaleDateString()}</td>
              <td>{forecast.temperatureC}</td>
              <td>{forecast.temperatureF}</td>
              <td>{forecast.summary}</td>
            </tr>
          ))}
        </tbody>
      </Table>
    </div>
  );
}
