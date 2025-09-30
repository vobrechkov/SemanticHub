import { NextRequest, NextResponse } from 'next/server';

interface WeatherForecast {
  date: string;
  temperatureC: number;
  temperatureF: number;
  summary: string | null;
}

export async function GET(request: NextRequest) {
  try {
    const apiBaseUrl = process.env.KNOWLEDGE_API_URL || 'http://localhost:5000';
    const response = await fetch(`${apiBaseUrl}/weatherforecast`, {
      headers: {
        'Content-Type': 'application/json',
      },
      cache: 'no-store',
    });

    if (!response.ok) {
      return NextResponse.json(
        { error: 'Failed to fetch weather data' },
        { status: response.status }
      );
    }

    const data: WeatherForecast[] = await response.json();
    return NextResponse.json(data);
  } catch (error) {
    console.error('Error fetching weather data:', error);
    return NextResponse.json(
      { error: 'Internal server error' },
      { status: 500 }
    );
  }
}
